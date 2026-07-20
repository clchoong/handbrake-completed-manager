using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public enum FinalizationPromotionFaultPoint
{
    AfterIntentPersisted,
    AfterAtomicMove
}

public interface IFinalizationPromotionFaultInjector
{
    Task OnFaultPointAsync(FinalizationPromotionFaultPoint faultPoint);
}

public sealed class FinalizationPromotionService(
    ReplacementOperationRepository operationRepository,
    FinalizationTransactionRepository transactionRepository,
    IFinalizationPromotionFaultInjector? faultInjector = null)
{
    public async Task<FinalizationPromotionResult> PromoteAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await transactionRepository.InitializeAsync(cancellationToken);
        var operation = await operationRepository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("The replacement operation no longer exists.");
        var transaction = await transactionRepository.GetAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("A prepared finalisation transaction is required.");
        ValidateOperation(operation, transaction);

        if (transaction.Checkpoint == FinalizationCheckpoint.PromoteTemporaryIntentRecorded &&
            !File.Exists(operation.TemporaryPath) &&
            File.Exists(operation.FinalPath))
        {
            return await RecordRecoveredPromotionAsync(operation, transaction, cancellationToken);
        }

        if (transaction.Checkpoint is not (
                FinalizationCheckpoint.Prepared or
                FinalizationCheckpoint.PromoteTemporaryIntentRecorded))
        {
            throw new InvalidOperationException($"Atomic promotion is unavailable at checkpoint {transaction.Checkpoint}.");
        }

        var activeTransaction = transaction;
        var intentPersisted = transaction.Checkpoint == FinalizationCheckpoint.PromoteTemporaryIntentRecorded;
        PromotionLocks? locks = null;
        try
        {
            locks = await PromotionLocks.OpenAsync(operation, transaction, cancellationToken);
            if (!intentPersisted)
            {
                if (!await transactionRepository.TryTransitionAsync(
                        operationId,
                        transaction.Checkpoint,
                        transaction.Revision,
                        FinalizationCheckpoint.PromoteTemporaryIntentRecorded,
                        null,
                        DateTimeOffset.UtcNow,
                        cancellationToken))
                {
                    throw new InvalidOperationException("The finalisation transaction changed before promotion intent could be recorded.");
                }

                activeTransaction = transaction with
                {
                    Checkpoint = FinalizationCheckpoint.PromoteTemporaryIntentRecorded,
                    Revision = transaction.Revision + 1,
                    FailureMessage = null,
                    DateUpdatedUtc = DateTimeOffset.UtcNow
                };
                intentPersisted = true;
            }

            await InvokeFaultPointAsync(FinalizationPromotionFaultPoint.AfterIntentPersisted);
            if (PathsEqual(operation.SourcePath, operation.FinalPath))
            {
                // Windows ReplaceFile requires the source and destination handles to be
                // closed. Both files were verified while locked immediately above, and
                // the durable intent checkpoint makes an interruption recoverable.
                await locks.DisposeAsync();
                locks = null;
                File.Replace(operation.TemporaryPath, operation.FinalPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(operation.TemporaryPath, operation.FinalPath);
            }
            if (File.Exists(operation.TemporaryPath) || !File.Exists(operation.FinalPath))
            {
                throw new IOException("The atomic promotion did not produce the expected path state.");
            }

            await InvokeFaultPointAsync(FinalizationPromotionFaultPoint.AfterAtomicMove);
            if (!await transactionRepository.TryTransitionAsync(
                    operationId,
                    activeTransaction.Checkpoint,
                    activeTransaction.Revision,
                    FinalizationCheckpoint.FinalPromoted,
                    null,
                    DateTimeOffset.UtcNow,
                    cancellationToken))
            {
                throw new InvalidOperationException("The final file was promoted, but its completion checkpoint could not be recorded.");
            }

            return new FinalizationPromotionResult(operationId, operation.FinalPath, transaction.FinalSha256, false);
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
            if (locks is not null)
            {
                await locks.DisposeAsync();
            }
        }
    }

    private async Task<FinalizationPromotionResult> RecordRecoveredPromotionAsync(
        ReplacementOperation operation,
        FinalizationTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var locks = await PromotedFileLocks.OpenAsync(operation, transaction, cancellationToken);
            if (!await transactionRepository.TryTransitionAsync(
                    operation.Id,
                    transaction.Checkpoint,
                    transaction.Revision,
                    FinalizationCheckpoint.FinalPromoted,
                    null,
                    DateTimeOffset.UtcNow,
                    cancellationToken))
            {
                throw new InvalidOperationException("The recovered promotion checkpoint changed before it could be recorded.");
            }

            return new FinalizationPromotionResult(operation.Id, operation.FinalPath, transaction.FinalSha256, true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            await RecordFailureBestEffortAsync(transaction, exception);
            throw;
        }
    }

    private async Task RecordFailureBestEffortAsync(
        FinalizationTransaction transaction,
        Exception exception)
    {
        try
        {
            await transactionRepository.TryRecordFailureAsync(
                transaction.OperationId,
                transaction.Checkpoint,
                transaction.Revision,
                $"Atomic promotion requires recovery review: {exception.Message}",
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        }
        catch (Exception recordException) when (
            recordException is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Preserve the original file-operation failure. The intent checkpoint is still durable.
        }
    }

    private Task InvokeFaultPointAsync(FinalizationPromotionFaultPoint faultPoint) =>
        faultInjector?.OnFaultPointAsync(faultPoint) ?? Task.CompletedTask;

    private static void ValidateOperation(
        ReplacementOperation operation,
        FinalizationTransaction transaction)
    {
        if (transaction.OperationId != operation.Id ||
            operation.Status != ReplacementOperationStatus.InProgress ||
            operation.Stage != ReplacementOperationStage.BackingUpSource ||
            operation.VerificationStatus != ReplacementVerificationStatus.Verified ||
            operation.BytesCopied != operation.DestinationSize)
        {
            throw new InvalidOperationException("The replacement operation is not at the verified atomic-promotion boundary.");
        }

        var temporaryDirectory = Path.GetDirectoryName(Path.GetFullPath(operation.TemporaryPath));
        var finalDirectory = Path.GetDirectoryName(Path.GetFullPath(operation.FinalPath));
        if (temporaryDirectory is null || finalDirectory is null ||
            !string.Equals(temporaryDirectory, finalDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Atomic promotion requires temporary and final paths in the same directory.");
        }
    }

    private sealed class PromotionLocks : IAsyncDisposable
    {
        private PromotionLocks(FileStream source, FileStream backup, FileStream temporary)
        {
            Source = source;
            Backup = backup;
            Temporary = temporary;
        }

        private FileStream Source { get; }
        private FileStream Backup { get; }
        private FileStream Temporary { get; }

        public static async Task<PromotionLocks> OpenAsync(
            ReplacementOperation operation,
            FinalizationTransaction transaction,
            CancellationToken cancellationToken)
        {
            var replacesSourceInPlace = PathsEqual(operation.SourcePath, operation.FinalPath);
            if (!replacesSourceInPlace)
            {
                EnsureUnoccupied(operation.FinalPath);
            }
            FileStream? source = null;
            FileStream? backup = null;
            FileStream? temporary = null;
            try
            {
                source = OpenReadLock(operation.SourcePath, allowRename: replacesSourceInPlace);
                backup = OpenReadLock(operation.BackupPath, allowRename: false);
                temporary = OpenReadLock(operation.TemporaryPath, allowRename: true);
                await VerifyAsync(source, operation.SourceSize, transaction.SourceSha256, "source", cancellationToken);
                await VerifyAsync(backup, operation.SourceSize, transaction.SourceSha256, "original backup", cancellationToken);
                await VerifyAsync(temporary, operation.DestinationSize, transaction.FinalSha256, "temporary copy", cancellationToken);
                if (!replacesSourceInPlace)
                {
                    EnsureUnoccupied(operation.FinalPath);
                }
                return new PromotionLocks(source, backup, temporary);
            }
            catch
            {
                if (temporary is not null) await temporary.DisposeAsync();
                if (backup is not null) await backup.DisposeAsync();
                if (source is not null) await source.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Temporary.DisposeAsync();
            await Backup.DisposeAsync();
            await Source.DisposeAsync();
        }
    }

    private sealed class PromotedFileLocks : IAsyncDisposable
    {
        private PromotedFileLocks(FileStream? source, FileStream backup, FileStream final)
        {
            Source = source;
            Backup = backup;
            Final = final;
        }

        private FileStream? Source { get; }
        private FileStream Backup { get; }
        private FileStream Final { get; }

        public static async Task<PromotedFileLocks> OpenAsync(
            ReplacementOperation operation,
            FinalizationTransaction transaction,
            CancellationToken cancellationToken)
        {
            if (File.Exists(operation.TemporaryPath) || Directory.Exists(operation.TemporaryPath))
            {
                throw new InvalidOperationException("Recovery is ambiguous because the temporary path is still occupied.");
            }

            FileStream? source = null;
            FileStream? backup = null;
            FileStream? final = null;
            try
            {
                backup = OpenReadLock(operation.BackupPath, allowRename: false);
                final = OpenReadLock(operation.FinalPath, allowRename: false);
                await VerifyAsync(backup, operation.SourceSize, transaction.SourceSha256, "original backup", cancellationToken);
                await VerifyAsync(final, operation.DestinationSize, transaction.FinalSha256, "promoted final file", cancellationToken);
                if (!PathsEqual(operation.SourcePath, operation.FinalPath))
                {
                    source = OpenReadLock(operation.SourcePath, allowRename: false);
                    await VerifyAsync(source, operation.SourceSize, transaction.SourceSha256, "source", cancellationToken);
                }
                return new PromotedFileLocks(source, backup, final);
            }
            catch
            {
                if (final is not null) await final.DisposeAsync();
                if (backup is not null) await backup.DisposeAsync();
                if (source is not null) await source.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Final.DisposeAsync();
            await Backup.DisposeAsync();
            if (Source is not null) await Source.DisposeAsync();
        }
    }

    private static FileStream OpenReadLock(string path, bool allowRename) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        allowRename ? FileShare.Read | FileShare.Delete : FileShare.Read,
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
            throw new InvalidOperationException($"The {artifactName} length changed before atomic promotion.");
        }

        stream.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The {artifactName} SHA-256 digest changed before atomic promotion.");
        }
    }

    private static void EnsureUnoccupied(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"The final path is occupied and will not be overwritten: {path}");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
