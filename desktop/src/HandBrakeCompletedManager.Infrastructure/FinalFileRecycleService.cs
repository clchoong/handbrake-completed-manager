using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public enum FinalFileRecycleFaultPoint
{
    AfterIntentPersisted,
    AfterRecycleCompleted
}

public interface IFinalFileRecycleFaultInjector
{
    Task OnFaultPointAsync(FinalFileRecycleFaultPoint faultPoint);
}

public sealed class FinalFileRecycleService(
    ReplacementOperationRepository operationRepository,
    FinalizationTransactionRepository transactionRepository,
    IRecoverableFileRecycler fileRecycler,
    IFinalFileRecycleFaultInjector? faultInjector = null)
{
    public async Task<FinalFileRecycleResult> RecycleAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await transactionRepository.InitializeAsync(cancellationToken);
        var operation = await operationRepository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("The replacement operation no longer exists.");
        var transaction = await transactionRepository.GetAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("A prepared undo transaction is required before final-file recycling.");
        ValidateOperation(operation, transaction);

        if (transaction.Checkpoint == FinalizationCheckpoint.RecycleFinalIntentRecorded &&
            !File.Exists(operation.FinalPath) &&
            !Directory.Exists(operation.FinalPath))
        {
            return await RecordRecoveredRecycleAsync(operation, transaction, cancellationToken);
        }

        if (transaction.Checkpoint is not (
                FinalizationCheckpoint.SourceRestored or
                FinalizationCheckpoint.RecycleFinalIntentRecorded))
        {
            throw new InvalidOperationException($"Final-file recycling is unavailable at checkpoint {transaction.Checkpoint}.");
        }

        var activeTransaction = transaction;
        var intentPersisted = transaction.Checkpoint == FinalizationCheckpoint.RecycleFinalIntentRecorded;
        FileStream? source = null;
        FileStream? backup = null;
        FileStream? final = null;
        try
        {
            EnsureUnoccupied(operation.TemporaryPath, "temporary copy");
            source = OpenReadLock(operation.SourcePath, allowRecycle: false);
            backup = OpenReadLock(operation.BackupPath, allowRecycle: false);
            final = OpenReadLock(operation.FinalPath, allowRecycle: true);
            await VerifyAsync(source, operation.SourceSize, transaction.SourceSha256, "restored source", cancellationToken);
            await VerifyAsync(backup, operation.SourceSize, transaction.SourceSha256, "original backup", cancellationToken);
            await VerifyAsync(final, operation.DestinationSize, transaction.FinalSha256, "promoted final file", cancellationToken);

            if (!intentPersisted)
            {
                if (!await transactionRepository.TryTransitionAsync(
                        operationId, transaction.Checkpoint, transaction.Revision,
                        FinalizationCheckpoint.RecycleFinalIntentRecorded, null,
                        DateTimeOffset.UtcNow, cancellationToken))
                {
                    throw new InvalidOperationException("The undo transaction changed before final-file recycling intent could be recorded.");
                }

                activeTransaction = transaction with
                {
                    Checkpoint = FinalizationCheckpoint.RecycleFinalIntentRecorded,
                    Revision = transaction.Revision + 1,
                    FailureMessage = null,
                    DateUpdatedUtc = DateTimeOffset.UtcNow
                };
                intentPersisted = true;
            }

            await InvokeFaultPointAsync(FinalFileRecycleFaultPoint.AfterIntentPersisted);
            await fileRecycler.RecycleAsync(operation.FinalPath, cancellationToken);
            if (File.Exists(operation.FinalPath) || Directory.Exists(operation.FinalPath))
            {
                throw new IOException("The promoted final path remains occupied after the Recycle Bin operation.");
            }

            await InvokeFaultPointAsync(FinalFileRecycleFaultPoint.AfterRecycleCompleted);
            if (!await transactionRepository.TryTransitionAsync(
                    operationId, activeTransaction.Checkpoint, activeTransaction.Revision,
                    FinalizationCheckpoint.FinalRecycled, null,
                    DateTimeOffset.UtcNow, cancellationToken))
            {
                throw new InvalidOperationException("The final file was recycled, but its completion checkpoint could not be recorded.");
            }

            return new FinalFileRecycleResult(operationId, operation.FinalPath, false);
        }
        catch (Exception exception) when (
            intentPersisted &&
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            await RecordFailureBestEffortAsync(activeTransaction, exception);
            throw;
        }
        finally
        {
            if (final is not null) await final.DisposeAsync();
            if (backup is not null) await backup.DisposeAsync();
            if (source is not null) await source.DisposeAsync();
        }
    }

    private async Task<FinalFileRecycleResult> RecordRecoveredRecycleAsync(
        ReplacementOperation operation,
        FinalizationTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            EnsureUnoccupied(operation.TemporaryPath, "temporary copy");
            await using var source = OpenReadLock(operation.SourcePath, allowRecycle: false);
            await using var backup = OpenReadLock(operation.BackupPath, allowRecycle: false);
            await VerifyAsync(source, operation.SourceSize, transaction.SourceSha256, "restored source", cancellationToken);
            await VerifyAsync(backup, operation.SourceSize, transaction.SourceSha256, "original backup", cancellationToken);
            if (!await transactionRepository.TryTransitionAsync(
                    operation.Id, transaction.Checkpoint, transaction.Revision,
                    FinalizationCheckpoint.FinalRecycled, null,
                    DateTimeOffset.UtcNow, cancellationToken))
            {
                throw new InvalidOperationException("The recovered final-file recycling checkpoint changed before it could be recorded.");
            }

            return new FinalFileRecycleResult(operation.Id, operation.FinalPath, true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            await RecordFailureBestEffortAsync(transaction, exception);
            throw;
        }
    }

    private async Task RecordFailureBestEffortAsync(FinalizationTransaction transaction, Exception exception)
    {
        try
        {
            await transactionRepository.TryRecordFailureAsync(
                transaction.OperationId, transaction.Checkpoint, transaction.Revision,
                $"Final-file recycling requires recovery review: {exception.Message}",
                DateTimeOffset.UtcNow, CancellationToken.None);
        }
        catch (Exception recordException) when (
            recordException is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Preserve the original failure; the durable intent remains available for recovery.
        }
    }

    private Task InvokeFaultPointAsync(FinalFileRecycleFaultPoint faultPoint) =>
        faultInjector?.OnFaultPointAsync(faultPoint) ?? Task.CompletedTask;

    private static void ValidateOperation(ReplacementOperation operation, FinalizationTransaction transaction)
    {
        if (transaction.OperationId != operation.Id ||
            operation.Status != ReplacementOperationStatus.Completed ||
            operation.Stage != ReplacementOperationStage.Completed ||
            operation.VerificationStatus != ReplacementVerificationStatus.Verified ||
            operation.BytesCopied != operation.DestinationSize)
        {
            throw new InvalidOperationException("The replacement operation is not at the verified final-file recycling boundary.");
        }

        string[] paths =
        [
            Path.GetFullPath(operation.SourcePath),
            Path.GetFullPath(operation.TemporaryPath),
            Path.GetFullPath(operation.BackupPath),
            Path.GetFullPath(operation.FinalPath)
        ];
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
        {
            throw new InvalidOperationException("Undo requires distinct source, temporary, backup, and final paths.");
        }
    }

    private static FileStream OpenReadLock(string path, bool allowRecycle) => new(
        path, FileMode.Open, FileAccess.Read,
        allowRecycle ? FileShare.Read | FileShare.Delete : FileShare.Read,
        1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task VerifyAsync(
        FileStream stream, long expectedLength, string expectedSha256,
        string artifactName, CancellationToken cancellationToken)
    {
        if (stream.Length != expectedLength)
        {
            throw new InvalidOperationException($"The {artifactName} length does not match the undo transaction.");
        }

        stream.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The {artifactName} SHA-256 digest does not match the undo transaction.");
        }
    }

    private static void EnsureUnoccupied(string path, string artifactName)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"The {artifactName} path must be empty during undo: {path}");
        }
    }
}
