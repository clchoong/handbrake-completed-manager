using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public enum SourceRestorationFaultPoint
{
    AfterIntentPersisted,
    AfterRestoreFileVerified,
    AfterAtomicMove
}

public interface ISourceRestorationFaultInjector
{
    Task OnFaultPointAsync(SourceRestorationFaultPoint faultPoint);
}

public sealed class SourceRestorationService(
    ReplacementOperationRepository operationRepository,
    FinalizationTransactionRepository transactionRepository,
    ISourceRestorationFaultInjector? faultInjector = null)
{
    private const int BufferSize = 1024 * 1024;

    public async Task<SourceRestorationResult> RestoreAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await transactionRepository.InitializeAsync(cancellationToken);
        var operation = await operationRepository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("The replacement operation no longer exists.");
        var transaction = await transactionRepository.GetAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("A prepared undo transaction is required.");
        ValidateOperation(operation, transaction);
        var restorePath = GetRestoreTemporaryPath(operation);

        if (transaction.Checkpoint == FinalizationCheckpoint.RestoreSourceIntentRecorded &&
            File.Exists(operation.SourcePath) &&
            !File.Exists(restorePath) &&
            !Directory.Exists(restorePath))
        {
            return await RecordRecoveredRestorationAsync(operation, transaction, cancellationToken);
        }

        if (transaction.Checkpoint is not (
                FinalizationCheckpoint.UndoPrepared or
                FinalizationCheckpoint.RestoreSourceIntentRecorded))
        {
            throw new InvalidOperationException($"Source restoration is unavailable at checkpoint {transaction.Checkpoint}.");
        }

        EnsureUnoccupied(operation.SourcePath, "source");
        var activeTransaction = transaction;
        var intentPersisted = transaction.Checkpoint == FinalizationCheckpoint.RestoreSourceIntentRecorded;
        var resumed = File.Exists(restorePath);
        FileStream? backup = null;
        FileStream? final = null;
        FileStream? restore = null;
        try
        {
            backup = OpenReadLock(operation.BackupPath);
            final = OpenReadLock(operation.FinalPath);
            await VerifyAsync(backup, operation.SourceSize, transaction.SourceSha256, "original backup", cancellationToken);
            await VerifyAsync(final, operation.DestinationSize, transaction.FinalSha256, "promoted final file", cancellationToken);
            EnsureUnoccupied(operation.SourcePath, "source");

            if (!intentPersisted)
            {
                if (!await transactionRepository.TryTransitionAsync(
                        operationId,
                        transaction.Checkpoint,
                        transaction.Revision,
                        FinalizationCheckpoint.RestoreSourceIntentRecorded,
                        null,
                        DateTimeOffset.UtcNow,
                        cancellationToken))
                {
                    throw new InvalidOperationException("The finalisation transaction changed before source-restoration intent could be recorded.");
                }

                activeTransaction = transaction with
                {
                    Checkpoint = FinalizationCheckpoint.RestoreSourceIntentRecorded,
                    Revision = transaction.Revision + 1,
                    FailureMessage = null,
                    DateUpdatedUtc = DateTimeOffset.UtcNow
                };
                intentPersisted = true;
            }

            await InvokeFaultPointAsync(SourceRestorationFaultPoint.AfterIntentPersisted);
            restore = OpenRestoreFile(restorePath);
            await ValidateAndCompleteRestoreFileAsync(backup, restore, operation.SourceSize, cancellationToken);
            await VerifyAsync(restore, operation.SourceSize, transaction.SourceSha256, "restored source copy", cancellationToken);
            await InvokeFaultPointAsync(SourceRestorationFaultPoint.AfterRestoreFileVerified);

            EnsureUnoccupied(operation.SourcePath, "source");
            File.Move(restorePath, operation.SourcePath);
            if (File.Exists(restorePath) || !File.Exists(operation.SourcePath))
            {
                throw new IOException("The atomic source restoration did not produce the expected path state.");
            }

            await InvokeFaultPointAsync(SourceRestorationFaultPoint.AfterAtomicMove);
            if (!await transactionRepository.TryTransitionAsync(
                    operationId,
                    activeTransaction.Checkpoint,
                    activeTransaction.Revision,
                    FinalizationCheckpoint.SourceRestored,
                    null,
                    DateTimeOffset.UtcNow,
                    cancellationToken))
            {
                throw new InvalidOperationException("The source was restored, but its completion checkpoint could not be recorded.");
            }

            return new SourceRestorationResult(operationId, operation.SourcePath, transaction.SourceSha256, false, resumed);
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
            if (restore is not null) await restore.DisposeAsync();
            if (final is not null) await final.DisposeAsync();
            if (backup is not null) await backup.DisposeAsync();
        }
    }

    public static string GetRestoreTemporaryPath(ReplacementOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return $"{Path.GetFullPath(operation.SourcePath)}.{operation.Id:N}.hbcm-restoring";
    }

    private async Task<SourceRestorationResult> RecordRecoveredRestorationAsync(
        ReplacementOperation operation,
        FinalizationTransaction transaction,
        CancellationToken cancellationToken)
    {
        FileStream? source = null;
        FileStream? backup = null;
        FileStream? final = null;
        try
        {
            source = OpenReadLock(operation.SourcePath);
            backup = OpenReadLock(operation.BackupPath);
            final = OpenReadLock(operation.FinalPath);
            await VerifyAsync(source, operation.SourceSize, transaction.SourceSha256, "restored source", cancellationToken);
            await VerifyAsync(backup, operation.SourceSize, transaction.SourceSha256, "original backup", cancellationToken);
            await VerifyAsync(final, operation.DestinationSize, transaction.FinalSha256, "promoted final file", cancellationToken);
            if (!await transactionRepository.TryTransitionAsync(
                    operation.Id,
                    transaction.Checkpoint,
                    transaction.Revision,
                    FinalizationCheckpoint.SourceRestored,
                    null,
                    DateTimeOffset.UtcNow,
                    cancellationToken))
            {
                throw new InvalidOperationException("The recovered source-restoration checkpoint changed before it could be recorded.");
            }

            return new SourceRestorationResult(operation.Id, operation.SourcePath, transaction.SourceSha256, true, false);
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
            if (source is not null) await source.DisposeAsync();
        }
    }

    private static async Task ValidateAndCompleteRestoreFileAsync(
        FileStream backup,
        FileStream restore,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        if (restore.Length > expectedLength)
        {
            throw new InvalidOperationException("The partial source-restoration file is larger than the verified backup.");
        }

        var prefixLength = restore.Length;
        backup.Position = 0;
        restore.Position = 0;
        var backupBuffer = new byte[BufferSize];
        var restoreBuffer = new byte[BufferSize];
        long compared = 0;
        while (compared < prefixLength)
        {
            var requested = (int)Math.Min(BufferSize, prefixLength - compared);
            var backupRead = await ReadExactlyUpToAsync(backup, backupBuffer, requested, cancellationToken);
            var restoreRead = await ReadExactlyUpToAsync(restore, restoreBuffer, requested, cancellationToken);
            if (backupRead != requested || restoreRead != requested ||
                !backupBuffer.AsSpan(0, requested).SequenceEqual(restoreBuffer.AsSpan(0, requested)))
            {
                throw new InvalidOperationException("The partial source-restoration file does not match the verified backup and will not be resumed.");
            }

            compared += requested;
        }

        backup.Position = prefixLength;
        restore.Position = prefixLength;
        await backup.CopyToAsync(restore, BufferSize, cancellationToken);
        await restore.FlushAsync(cancellationToken);
        restore.Flush(flushToDisk: true);
    }

    private static async Task<int> ReadExactlyUpToAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken);
            if (read == 0) break;
            total += read;
        }

        return total;
    }

    private async Task RecordFailureBestEffortAsync(FinalizationTransaction transaction, Exception exception)
    {
        try
        {
            await transactionRepository.TryRecordFailureAsync(
                transaction.OperationId,
                transaction.Checkpoint,
                transaction.Revision,
                $"Source restoration requires recovery review: {exception.Message}",
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        }
        catch (Exception recordException) when (
            recordException is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Preserve the original file-operation failure. The intent checkpoint is still durable.
        }
    }

    private Task InvokeFaultPointAsync(SourceRestorationFaultPoint faultPoint) =>
        faultInjector?.OnFaultPointAsync(faultPoint) ?? Task.CompletedTask;

    private static void ValidateOperation(ReplacementOperation operation, FinalizationTransaction transaction)
    {
        var validOperationBoundary =
            (operation.Status == ReplacementOperationStatus.Completed &&
             operation.Stage == ReplacementOperationStage.Completed) ||
            (operation.Status == ReplacementOperationStatus.InProgress &&
             operation.Stage == ReplacementOperationStage.BackingUpSource);
        if (transaction.OperationId != operation.Id ||
            !validOperationBoundary ||
            operation.VerificationStatus != ReplacementVerificationStatus.Verified ||
            operation.BytesCopied != operation.DestinationSize)
        {
            throw new InvalidOperationException("The replacement operation is not at the verified source-restoration boundary.");
        }

        var source = Path.GetFullPath(operation.SourcePath);
        var backup = Path.GetFullPath(operation.BackupPath);
        var final = Path.GetFullPath(operation.FinalPath);
        var restore = GetRestoreTemporaryPath(operation);
        if (string.Equals(source, backup, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, final, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(backup, final, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(restore, backup, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(restore, final, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Source restoration requires distinct source, restore, backup, and final paths.");
        }
    }

    private static FileStream OpenReadLock(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static FileStream OpenRestoreFile(string path) => new(
        path,
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.Delete,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

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

    private static void EnsureUnoccupied(string path, string artifactName)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"The {artifactName} path is occupied and will not be overwritten: {path}");
        }
    }
}
