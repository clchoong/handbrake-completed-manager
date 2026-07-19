namespace HandBrakeCompletedManager.Core;

public sealed record ReplacementCopyProgress(
    Guid OperationId,
    long BytesCopied,
    long TotalBytes)
{
    public double Percentage => TotalBytes <= 0 ? 0 : BytesCopied * 100d / TotalBytes;
}

public sealed record TemporaryCopyResult(
    Guid OperationId,
    string TemporaryPath,
    long BytesCopied,
    string Sha256);

public static class ReplacementOperationFactory
{
    public static ReplacementOperation CreatePlanned(
        ReplacementPlan plan,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanProceed || plan.Snapshot.SourceSize is not > 0 || plan.Snapshot.DestinationSize is not > 0)
        {
            throw new InvalidOperationException("A replacement operation requires a passing preflight plan with readable non-empty files.");
        }

        return new ReplacementOperation(
            Guid.NewGuid(),
            plan.CompletedEncode.Id,
            ReplacementOperationStatus.Planned,
            ReplacementOperationStage.Preparing,
            plan.CompletedEncode.SourcePath,
            plan.CompletedEncode.DestinationPath,
            plan.Paths.FinalPath,
            plan.Paths.TemporaryPath,
            plan.Paths.BackupPath,
            plan.Snapshot.SourceSize.Value,
            plan.Snapshot.DestinationSize.Value,
            0,
            ReplacementVerificationStatus.NotVerified,
            null,
            createdAtUtc.ToUniversalTime(),
            createdAtUtc.ToUniversalTime());
    }
}
