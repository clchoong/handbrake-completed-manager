using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class OutputRecycleService(
    CompletedEncodeRepository completedEncodeRepository,
    ReplacementOperationRepository replacementOperationRepository,
    FinalizationTransactionRepository finalizationTransactionRepository,
    IRecoverableFileRecycler fileRecycler)
{
    public async Task<OutputRecycleResult> RecycleAsync(
        CompletedEncode record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await completedEncodeRepository.InitializeAsync(cancellationToken);
        ValidateRecordedOutput(record);
        await EnsureReplacementDoesNotNeedOutputAsync(record.Id, cancellationToken);

        var outputPath = Path.GetFullPath(record.DestinationPath);
        await using var outputLock = new FileStream(
            outputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var expectedSize = record.DestinationSize!.Value;
        var expectedLastWriteUtc = record.DestinationLastWriteUtc!.Value.ToUniversalTime();
        if (outputLock.Length != expectedSize ||
            File.GetLastWriteTimeUtc(outputPath) != expectedLastWriteUtc.UtcDateTime)
        {
            throw new InvalidOperationException(
                "The output changed after it was recorded. Refresh or encode it again before recycling it.");
        }

        await fileRecycler.RecycleAsync(outputPath, cancellationToken);
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new IOException("Windows did not move the output to the Recycle Bin.");
        }

        if (!await completedEncodeRepository.TryMarkDestinationRecycledAsync(
                record.Id,
                outputPath,
                expectedSize,
                expectedLastWriteUtc,
                DateTimeOffset.UtcNow,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "The output was recycled, but the history record changed before it could be updated. Refresh the history.");
        }

        return new OutputRecycleResult(record.Id, outputPath);
    }

    private async Task EnsureReplacementDoesNotNeedOutputAsync(
        Guid completedEncodeId,
        CancellationToken cancellationToken)
    {
        var operation = await replacementOperationRepository.GetLatestForCompletedEncodeAsync(
            completedEncodeId,
            cancellationToken);
        if (operation is null)
        {
            return;
        }

        var transaction = await finalizationTransactionRepository.GetAsync(operation.Id, cancellationToken);
        if (transaction?.Checkpoint is FinalizationCheckpoint.Completed or FinalizationCheckpoint.Undone)
        {
            return;
        }

        if (operation.Status == ReplacementOperationStatus.Cancelled && transaction is null)
        {
            return;
        }

        throw new InvalidOperationException(
            "This output is still linked to unfinished replacement work. Finish or resolve that work before recycling the output.");
    }

    private static void ValidateRecordedOutput(CompletedEncode record)
    {
        if (!record.DestinationExists || record.DestinationSize is null || record.DestinationLastWriteUtc is null)
        {
            throw new InvalidOperationException("The recorded output is not available for recycling.");
        }

        var sourcePath = Path.GetFullPath(record.SourcePath);
        var outputPath = Path.GetFullPath(record.DestinationPath);
        if (string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The source and output resolve to the same path; recycling is blocked.");
        }

        if (!File.Exists(outputPath))
        {
            throw new FileNotFoundException("The recorded output no longer exists.", outputPath);
        }
    }
}
