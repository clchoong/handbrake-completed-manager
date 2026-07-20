using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class UndoPreparationService(
    ReplacementOperationRepository operationRepository,
    FinalizationTransactionRepository transactionRepository)
{
    public async Task<UndoPreparationResult> PrepareAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await transactionRepository.InitializeAsync(cancellationToken);
        var operation = await operationRepository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("The replacement operation no longer exists.");
        var transaction = await transactionRepository.GetAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("A completed finalisation transaction is required before undo.");
        if (operation.Status != ReplacementOperationStatus.Completed ||
            operation.Stage != ReplacementOperationStage.Completed ||
            operation.VerificationStatus != ReplacementVerificationStatus.Verified ||
            transaction.Checkpoint is not (FinalizationCheckpoint.Completed or FinalizationCheckpoint.UndoPrepared))
        {
            throw new InvalidOperationException("Undo preparation is unavailable at the current replacement boundary.");
        }

        EnsureUnoccupied(operation.SourcePath, "source");
        EnsureUnoccupied(operation.TemporaryPath, "temporary copy");
        await using var backup = OpenReadLock(operation.BackupPath);
        await using var final = OpenReadLock(operation.FinalPath);
        await VerifyAsync(backup, operation.SourceSize, transaction.SourceSha256, "original backup", cancellationToken);
        await VerifyAsync(final, operation.DestinationSize, transaction.FinalSha256, "promoted final file", cancellationToken);
        EnsureUnoccupied(operation.SourcePath, "source");
        EnsureUnoccupied(operation.TemporaryPath, "temporary copy");

        if (transaction.Checkpoint == FinalizationCheckpoint.UndoPrepared)
        {
            return new UndoPreparationResult(operationId, true);
        }

        if (!await transactionRepository.TryTransitionAsync(
                operationId,
                transaction.Checkpoint,
                transaction.Revision,
                FinalizationCheckpoint.UndoPrepared,
                null,
                DateTimeOffset.UtcNow,
                cancellationToken))
        {
            throw new InvalidOperationException("The completed transaction changed before undo preparation could be recorded.");
        }

        return new UndoPreparationResult(operationId, false);
    }

    private static FileStream OpenReadLock(string path) => new(
        path, FileMode.Open, FileAccess.Read, FileShare.Read,
        1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task VerifyAsync(
        FileStream stream,
        long expectedLength,
        string expectedSha256,
        string artifactName,
        CancellationToken cancellationToken)
    {
        if (stream.Length != expectedLength)
        {
            throw new InvalidOperationException($"The {artifactName} length does not match the completed transaction.");
        }

        stream.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The {artifactName} SHA-256 digest does not match the completed transaction.");
        }
    }

    private static void EnsureUnoccupied(string path, string artifactName)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"The {artifactName} path must be empty before undo can be prepared: {path}");
        }
    }
}
