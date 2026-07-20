using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public enum SourceRecycleFaultPoint
{
    AfterIntentPersisted,
    AfterRecycleCompleted
}

public interface ISourceRecycleFaultInjector
{
    Task OnFaultPointAsync(SourceRecycleFaultPoint faultPoint);
}

public interface IRecoverableFileRecycler
{
    Task RecycleAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class SourceRecycleService(
    ReplacementOperationRepository operationRepository,
    FinalizationTransactionRepository transactionRepository,
    IRecoverableFileRecycler fileRecycler,
    ISourceRecycleFaultInjector? faultInjector = null)
{
    public async Task<SourceRecycleResult> RecycleAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await transactionRepository.InitializeAsync(cancellationToken);
        var operation = await operationRepository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("The replacement operation no longer exists.");
        var transaction = await transactionRepository.GetAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("A finalisation transaction is required before source recycling.");
        ValidateOperation(operation, transaction);

        if (transaction.Checkpoint == FinalizationCheckpoint.RecycleSourceIntentRecorded &&
            !File.Exists(operation.SourcePath) &&
            !Directory.Exists(operation.SourcePath))
        {
            return await RecordRecoveredRecycleAsync(operation, transaction, cancellationToken);
        }

        if (transaction.Checkpoint is not (
                FinalizationCheckpoint.FinalPromoted or
                FinalizationCheckpoint.RecycleSourceIntentRecorded))
        {
            throw new InvalidOperationException($"Source recycling is unavailable at checkpoint {transaction.Checkpoint}.");
        }

        var activeTransaction = transaction;
        var intentPersisted = transaction.Checkpoint == FinalizationCheckpoint.RecycleSourceIntentRecorded;
        FileStream? source = null;
        FileStream? backup = null;
        FileStream? final = null;
        try
        {
            source = OpenReadLock(operation.SourcePath, allowRecycle: true);
            backup = OpenReadLock(operation.BackupPath, allowRecycle: false);
            final = OpenReadLock(operation.FinalPath, allowRecycle: false);
            await VerifyAsync(source, operation.SourceSize, transaction.SourceSha256, "source", cancellationToken);
            await VerifyAsync(backup, operation.SourceSize, transaction.SourceSha256, "original backup", cancellationToken);
            await VerifyAsync(final, operation.DestinationSize, transaction.FinalSha256, "promoted final file", cancellationToken);

            if (!intentPersisted)
            {
                if (!await transactionRepository.TryTransitionAsync(
                        operationId,
                        transaction.Checkpoint,
                        transaction.Revision,
                        FinalizationCheckpoint.RecycleSourceIntentRecorded,
                        null,
                        DateTimeOffset.UtcNow,
                        cancellationToken))
                {
                    throw new InvalidOperationException("The finalisation transaction changed before source-recycling intent could be recorded.");
                }

                activeTransaction = transaction with
                {
                    Checkpoint = FinalizationCheckpoint.RecycleSourceIntentRecorded,
                    Revision = transaction.Revision + 1,
                    FailureMessage = null,
                    DateUpdatedUtc = DateTimeOffset.UtcNow
                };
                intentPersisted = true;
            }

            await InvokeFaultPointAsync(SourceRecycleFaultPoint.AfterIntentPersisted);
            await fileRecycler.RecycleAsync(operation.SourcePath, cancellationToken);
            if (File.Exists(operation.SourcePath) || Directory.Exists(operation.SourcePath))
            {
                throw new IOException("The source path remains occupied after the Recycle Bin operation.");
            }

            await InvokeFaultPointAsync(SourceRecycleFaultPoint.AfterRecycleCompleted);
            if (!await transactionRepository.TryTransitionAsync(
                    operationId,
                    activeTransaction.Checkpoint,
                    activeTransaction.Revision,
                    FinalizationCheckpoint.SourceRecycled,
                    null,
                    DateTimeOffset.UtcNow,
                    cancellationToken))
            {
                throw new InvalidOperationException("The source was recycled, but its completion checkpoint could not be recorded.");
            }

            return new SourceRecycleResult(operationId, operation.SourcePath, false);
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

    private async Task<SourceRecycleResult> RecordRecoveredRecycleAsync(
        ReplacementOperation operation,
        FinalizationTransaction transaction,
        CancellationToken cancellationToken)
    {
        FileStream? backup = null;
        FileStream? final = null;
        try
        {
            backup = OpenReadLock(operation.BackupPath, allowRecycle: false);
            final = OpenReadLock(operation.FinalPath, allowRecycle: false);
            await VerifyAsync(backup, operation.SourceSize, transaction.SourceSha256, "original backup", cancellationToken);
            await VerifyAsync(final, operation.DestinationSize, transaction.FinalSha256, "promoted final file", cancellationToken);
            if (!await transactionRepository.TryTransitionAsync(
                    operation.Id,
                    transaction.Checkpoint,
                    transaction.Revision,
                    FinalizationCheckpoint.SourceRecycled,
                    null,
                    DateTimeOffset.UtcNow,
                    cancellationToken))
            {
                throw new InvalidOperationException("The recovered source-recycling checkpoint changed before it could be recorded.");
            }

            return new SourceRecycleResult(operation.Id, operation.SourcePath, true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            await RecordFailureBestEffortAsync(transaction, exception);
            throw;
        }
        finally
        {
            if (final is not null) await final.DisposeAsync();
            if (backup is not null) await backup.DisposeAsync();
        }
    }

    private async Task RecordFailureBestEffortAsync(FinalizationTransaction transaction, Exception exception)
    {
        try
        {
            await transactionRepository.TryRecordFailureAsync(
                transaction.OperationId,
                transaction.Checkpoint,
                transaction.Revision,
                $"Source recycling requires recovery review: {exception.Message}",
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        }
        catch (Exception recordException) when (
            recordException is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Preserve the original file-operation failure. The intent checkpoint is still durable.
        }
    }

    private Task InvokeFaultPointAsync(SourceRecycleFaultPoint faultPoint) =>
        faultInjector?.OnFaultPointAsync(faultPoint) ?? Task.CompletedTask;

    private static void ValidateOperation(ReplacementOperation operation, FinalizationTransaction transaction)
    {
        if (transaction.OperationId != operation.Id ||
            operation.Status != ReplacementOperationStatus.InProgress ||
            operation.VerificationStatus != ReplacementVerificationStatus.Verified ||
            operation.BytesCopied != operation.DestinationSize)
        {
            throw new InvalidOperationException("The replacement operation is not at the verified source-recycling boundary.");
        }

        var source = Path.GetFullPath(operation.SourcePath);
        var backup = Path.GetFullPath(operation.BackupPath);
        var final = Path.GetFullPath(operation.FinalPath);
        if (string.Equals(source, backup, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, final, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(backup, final, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Source recycling requires distinct source, backup, and final paths.");
        }
    }

    private static FileStream OpenReadLock(string path, bool allowRecycle) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        allowRecycle ? FileShare.Read | FileShare.Delete : FileShare.Read,
        bufferSize: 1024 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task VerifyAsync(
        FileStream stream,
        long expectedLength,
        string expectedSha256,
        string artifactName,
        CancellationToken cancellationToken)
    {
        if (stream.Length != expectedLength)
        {
            throw new InvalidOperationException($"The {artifactName} length does not match the prepared transaction.");
        }

        stream.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The {artifactName} SHA-256 digest does not match the prepared transaction.");
        }
    }
}
