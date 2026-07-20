using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class UndoCompletionService(
    ReplacementOperationRepository operationRepository,
    FinalizationTransactionRepository transactionRepository)
{
    public async Task<UndoCompletionResult> CompleteAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await transactionRepository.InitializeAsync(cancellationToken);
        var operation = await operationRepository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("The replacement operation no longer exists.");
        var transaction = await transactionRepository.GetAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("An undo transaction is required before completion.");
        if (operation.Status != ReplacementOperationStatus.Completed ||
            operation.Stage != ReplacementOperationStage.Completed ||
            transaction.Checkpoint is not (FinalizationCheckpoint.FinalRecycled or FinalizationCheckpoint.Undone))
        {
            throw new InvalidOperationException("Undo completion is unavailable at the current transaction boundary.");
        }

        EnsureUnoccupied(operation.FinalPath, "promoted final");
        EnsureUnoccupied(operation.TemporaryPath, "temporary copy");
        await using var source = OpenReadLock(operation.SourcePath);
        await using var backup = OpenReadLock(operation.BackupPath);
        await VerifyAsync(source, operation.SourceSize, transaction.SourceSha256, "restored source", cancellationToken);
        await VerifyAsync(backup, operation.SourceSize, transaction.SourceSha256, "original backup", cancellationToken);
        EnsureUnoccupied(operation.FinalPath, "promoted final");
        EnsureUnoccupied(operation.TemporaryPath, "temporary copy");

        if (transaction.Checkpoint == FinalizationCheckpoint.Undone)
        {
            return new UndoCompletionResult(operationId, operation.SourcePath, true);
        }

        if (!await transactionRepository.TryCompleteUndoAsync(
                operationId, transaction.Revision, DateTimeOffset.UtcNow, cancellationToken))
        {
            throw new InvalidOperationException("The undo transaction changed before completion could be recorded atomically.");
        }

        return new UndoCompletionResult(operationId, operation.SourcePath, false);
    }

    private static FileStream OpenReadLock(string path) => new(
        path, FileMode.Open, FileAccess.Read, FileShare.Read,
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
            throw new IOException($"The {artifactName} path must be empty before undo completion: {path}");
        }
    }
}
