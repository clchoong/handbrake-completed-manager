namespace HandBrakeCompletedManager.Core;

public sealed record FinalizationReadinessIssue(string Code, string Message);

public sealed record FinalizationReadinessResult(
    bool IsReady,
    string? TemporarySha256,
    string? SourceSha256,
    IReadOnlyList<FinalizationReadinessIssue> Issues);

public enum ReplacementRecoveryAction
{
    RetryTemporaryCopy,
    DiscardTemporaryArtifact,
    CreateOriginalBackup,
    DiscardBackupArtifact,
    CheckFinalizationReadiness,
    ManualReview
}

public sealed record ReplacementRecoveryItem(
    Guid OperationId,
    Guid CompletedEncodeId,
    string SourcePath,
    ReplacementRecoveryAction Action,
    string Summary,
    DateTimeOffset UpdatedUtc);

public sealed record ReplacementRecoveryDecision(
    bool ShouldDisplay,
    ReplacementRecoveryAction Action,
    string Summary);

public static class ReplacementRecoveryClassifier
{
    public static ReplacementRecoveryDecision Review(
        ReplacementOperation operation,
        bool temporaryExists,
        OriginalBackupState? backup,
        bool backupExists)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!temporaryExists &&
            !backupExists &&
            operation.Status == ReplacementOperationStatus.Cancelled &&
            operation.FailureMessage?.Contains("discarded by the user", StringComparison.OrdinalIgnoreCase) == true &&
            (backup is null || backup.Status == OriginalBackupStatus.Cancelled))
        {
            return new ReplacementRecoveryDecision(false, ReplacementRecoveryAction.ManualReview, string.Empty);
        }

        if (backupExists && backup?.Status != OriginalBackupStatus.Verified)
        {
            return Display(ReplacementRecoveryAction.DiscardBackupArtifact,
                "An incomplete original-backup artifact requires review or cleanup.");
        }

        if (temporaryExists && operation.VerificationStatus != ReplacementVerificationStatus.Verified)
        {
            return Display(ReplacementRecoveryAction.DiscardTemporaryArtifact,
                "An incomplete converted temporary artifact requires review or cleanup.");
        }

        if (temporaryExists && operation.VerificationStatus == ReplacementVerificationStatus.Verified)
        {
            if (backupExists && backup?.Status == OriginalBackupStatus.Verified)
            {
                return Display(ReplacementRecoveryAction.CheckFinalizationReadiness,
                    "Verified temporary and original-backup artifacts are ready for a non-destructive finalisation check.");
            }

            if (backup is null || backup.Status is OriginalBackupStatus.Failed or OriginalBackupStatus.Cancelled)
            {
                return Display(ReplacementRecoveryAction.CreateOriginalBackup,
                    "The converted temporary copy is verified; original-backup preparation can continue.");
            }
        }

        if (!temporaryExists &&
            operation.Status is ReplacementOperationStatus.Failed or
                ReplacementOperationStatus.Cancelled or
                ReplacementOperationStatus.Planned)
        {
            return Display(ReplacementRecoveryAction.RetryTemporaryCopy,
                "No temporary artifact remains; a fresh preflight can determine whether copying may be retried.");
        }

        return Display(ReplacementRecoveryAction.ManualReview,
            "Persisted replacement state and current artifacts require manual review.");
    }

    private static ReplacementRecoveryDecision Display(ReplacementRecoveryAction action, string summary) =>
        new(true, action, summary);
}
