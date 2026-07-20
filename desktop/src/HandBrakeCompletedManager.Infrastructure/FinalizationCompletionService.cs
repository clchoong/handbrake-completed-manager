using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public enum FinalizationCompletionFaultPoint
{
    AfterArtifactsVerified,
    AfterDatabaseCommit
}

public interface IFinalizationCompletionFaultInjector
{
    Task OnFaultPointAsync(FinalizationCompletionFaultPoint faultPoint);
}

public sealed class FinalizationCompletionService(
    ReplacementOperationRepository operationRepository,
    FinalizationTransactionRepository transactionRepository,
    IFinalizationCompletionFaultInjector? faultInjector = null)
{
    public async Task<FinalizationCompletionResult> CompleteAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await transactionRepository.InitializeAsync(cancellationToken);
        var operation = await operationRepository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("The replacement operation no longer exists.");
        var transaction = await transactionRepository.GetAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("A finalisation transaction is required before completion.");
        ValidateOperation(operation, transaction);
        var committed = false;
        try
        {
            EnsureUnoccupied(operation.TemporaryPath, "temporary copy");
            await using var backup = OpenReadLock(operation.BackupPath);
            await using var final = OpenReadLock(operation.FinalPath);
            await VerifyAsync(backup, operation.SourceSize, transaction.SourceSha256, "original backup", cancellationToken);
            await VerifyAsync(final, operation.DestinationSize, transaction.FinalSha256, "promoted final file", cancellationToken);
            if (!PathsEqual(operation.SourcePath, operation.FinalPath))
            {
                EnsureUnoccupied(operation.SourcePath, "source");
            }
            EnsureUnoccupied(operation.TemporaryPath, "temporary copy");

            if (transaction.Checkpoint == FinalizationCheckpoint.Completed)
            {
                return new FinalizationCompletionResult(operationId, operation.FinalPath, true);
            }

            await InvokeFaultPointAsync(FinalizationCompletionFaultPoint.AfterArtifactsVerified);
            if (!await transactionRepository.TryCompleteForwardAsync(
                    operationId,
                    transaction.Revision,
                    DateTimeOffset.UtcNow,
                    cancellationToken))
            {
                throw new InvalidOperationException("The forward finalisation state changed before completion could be recorded atomically.");
            }

            committed = true;
            await InvokeFaultPointAsync(FinalizationCompletionFaultPoint.AfterDatabaseCommit);
            return new FinalizationCompletionResult(operationId, operation.FinalPath, false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            if (!committed && transaction.Checkpoint == FinalizationCheckpoint.SourceRecycled)
            {
                await RecordFailureBestEffortAsync(transaction, exception);
            }

            throw;
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
                $"Finalisation completion requires recovery review: {exception.Message}",
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        }
        catch (Exception recordException) when (
            recordException is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Preserve the original completion failure. SourceRecycled remains a stable checkpoint.
        }
    }

    private Task InvokeFaultPointAsync(FinalizationCompletionFaultPoint faultPoint) =>
        faultInjector?.OnFaultPointAsync(faultPoint) ?? Task.CompletedTask;

    private static void ValidateOperation(ReplacementOperation operation, FinalizationTransaction transaction)
    {
        var isReadyToComplete =
            transaction.Checkpoint == FinalizationCheckpoint.SourceRecycled &&
            operation.Status == ReplacementOperationStatus.InProgress &&
            operation.Stage == ReplacementOperationStage.BackingUpSource;
        var isAlreadyComplete =
            transaction.Checkpoint == FinalizationCheckpoint.Completed &&
            operation.Status == ReplacementOperationStatus.Completed &&
            operation.Stage == ReplacementOperationStage.Completed;
        if (transaction.OperationId != operation.Id ||
            (!isReadyToComplete && !isAlreadyComplete) ||
            operation.VerificationStatus != ReplacementVerificationStatus.Verified ||
            operation.BytesCopied != operation.DestinationSize)
        {
            throw new InvalidOperationException("The replacement operation is not at a consistent forward-completion boundary.");
        }

        var source = Path.GetFullPath(operation.SourcePath);
        var temporary = Path.GetFullPath(operation.TemporaryPath);
        var backup = Path.GetFullPath(operation.BackupPath);
        var final = Path.GetFullPath(operation.FinalPath);
        if (string.Equals(temporary, source, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(temporary, backup, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(temporary, final, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(backup, source, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(backup, final, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Forward completion requires distinct temporary, backup, and final paths.");
        }
    }

    private static FileStream OpenReadLock(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
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

    private static void EnsureUnoccupied(string path, string artifactName)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"The {artifactName} path must be empty before finalisation can complete: {path}");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
