using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed record TemporaryCopyCleanupResult(
    Guid OperationId,
    string TemporaryPath,
    long BytesRemoved);

public sealed class TemporaryCopyCleanupService(
    ReplacementOperationRepository operationRepository,
    Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<TemporaryCopyCleanupResult> DiscardAsync(
        ReplacementPlan plan,
        ReplacementOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operation);
        ValidateOperation(plan, operation);

        if (Directory.Exists(operation.TemporaryPath))
        {
            throw new InvalidOperationException("The recorded temporary path is a directory and cannot be discarded as a file.");
        }

        if (!File.Exists(operation.TemporaryPath))
        {
            throw new FileNotFoundException(
                "The recorded temporary-copy file no longer exists.",
                operation.TemporaryPath);
        }

        await operationRepository.InitializeAsync(cancellationToken);
        var latest = await operationRepository.GetLatestForCompletedEncodeAsync(
            plan.CompletedEncode.Id,
            cancellationToken);
        if (latest is null || latest.Id != operation.Id || latest.DateUpdatedUtc != operation.DateUpdatedUtc)
        {
            throw new InvalidOperationException(
                "The replacement operation changed after it was reviewed. Refresh recovery state before cleanup.");
        }

        var bytesRemoved = new FileInfo(operation.TemporaryPath).Length;
        await using var exclusiveStream = new FileStream(
            operation.TemporaryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Delete,
            bufferSize: 1,
            FileOptions.None);
        var stateUpdated = await operationRepository.TryCancelForTemporaryCleanupAsync(
            operation.Id,
            operation.DateUpdatedUtc,
            operation.BytesCopied,
            _clock(),
            cancellationToken);
        if (!stateUpdated)
        {
            throw new InvalidOperationException(
                "The replacement operation changed while cleanup was starting. No file was removed.");
        }

        File.Delete(operation.TemporaryPath);
        if (File.Exists(operation.TemporaryPath))
        {
            throw new IOException("The temporary-copy file could not be removed.");
        }

        return new TemporaryCopyCleanupResult(
            operation.Id,
            operation.TemporaryPath,
            bytesRemoved);
    }

    private static void ValidateOperation(ReplacementPlan plan, ReplacementOperation operation)
    {
        if (operation.CompletedEncodeId != plan.CompletedEncode.Id ||
            !PathsEqual(operation.SourcePath, plan.CompletedEncode.SourcePath) ||
            !PathsEqual(operation.DestinationPath, plan.CompletedEncode.DestinationPath) ||
            !PathsEqual(operation.FinalPath, plan.Paths.FinalPath) ||
            !PathsEqual(operation.TemporaryPath, plan.Paths.TemporaryPath) ||
            !PathsEqual(operation.BackupPath, plan.Paths.BackupPath) ||
            !operation.TemporaryPath.EndsWith(
                ReplacementPlanner.TemporarySuffix,
                StringComparison.OrdinalIgnoreCase) ||
            PathsEqual(operation.TemporaryPath, operation.SourcePath) ||
            PathsEqual(operation.TemporaryPath, operation.DestinationPath) ||
            PathsEqual(operation.TemporaryPath, operation.FinalPath) ||
            PathsEqual(operation.TemporaryPath, operation.BackupPath))
        {
            throw new InvalidOperationException(
                "The recorded operation does not exactly match the reviewed temporary-copy plan.");
        }

        if (operation.Status == ReplacementOperationStatus.Completed)
        {
            throw new InvalidOperationException("A completed replacement operation cannot be cleaned up here.");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
}
