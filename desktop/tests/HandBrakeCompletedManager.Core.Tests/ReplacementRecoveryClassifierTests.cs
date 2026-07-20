using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class ReplacementRecoveryClassifierTests
{
    [Fact]
    public void Review_VerifiedArtifactsRecommendReadinessCheck()
    {
        var operation = CreateOperation(ReplacementOperationStatus.InProgress, ReplacementVerificationStatus.Verified);
        var decision = ReplacementRecoveryClassifier.Review(operation, true, CreateBackup(operation, OriginalBackupStatus.Verified), true);

        Assert.True(decision.ShouldDisplay);
        Assert.Equal(ReplacementRecoveryAction.CheckFinalizationReadiness, decision.Action);
    }

    [Fact]
    public void Review_PartialTemporaryArtifactRecommendsCleanup()
    {
        var operation = CreateOperation(ReplacementOperationStatus.Failed, ReplacementVerificationStatus.Failed);
        var decision = ReplacementRecoveryClassifier.Review(operation, true, null, false);

        Assert.Equal(ReplacementRecoveryAction.DiscardTemporaryArtifact, decision.Action);
    }

    [Fact]
    public void Review_VerifiedTemporaryWithoutBackupRecommendsBackup()
    {
        var operation = CreateOperation(ReplacementOperationStatus.InProgress, ReplacementVerificationStatus.Verified);
        var decision = ReplacementRecoveryClassifier.Review(operation, true, null, false);

        Assert.Equal(ReplacementRecoveryAction.CreateOriginalBackup, decision.Action);
    }

    [Fact]
    public void Review_PartialBackupTakesPriorityOverTemporaryState()
    {
        var operation = CreateOperation(ReplacementOperationStatus.InProgress, ReplacementVerificationStatus.Verified);
        var decision = ReplacementRecoveryClassifier.Review(operation, true, CreateBackup(operation, OriginalBackupStatus.Copying), true);

        Assert.Equal(ReplacementRecoveryAction.DiscardBackupArtifact, decision.Action);
    }

    [Fact]
    public void Review_FailedOperationWithoutArtifactsRecommendsFreshCopy()
    {
        var operation = CreateOperation(ReplacementOperationStatus.Failed, ReplacementVerificationStatus.Failed);
        var decision = ReplacementRecoveryClassifier.Review(operation, false, null, false);

        Assert.Equal(ReplacementRecoveryAction.RetryTemporaryCopy, decision.Action);
    }

    [Fact]
    public void Review_ExplicitlyDiscardedArtifactsAreHidden()
    {
        var operation = CreateOperation(
            ReplacementOperationStatus.Cancelled,
            ReplacementVerificationStatus.NotVerified,
            "Temporary copy discarded by the user after explicit confirmation.");
        var decision = ReplacementRecoveryClassifier.Review(operation, false, null, false);

        Assert.False(decision.ShouldDisplay);
    }

    [Fact]
    public void Review_InconsistentStateRequiresManualReview()
    {
        var operation = CreateOperation(ReplacementOperationStatus.InProgress, ReplacementVerificationStatus.Verified);
        var backup = CreateBackup(operation, OriginalBackupStatus.Verified);
        var decision = ReplacementRecoveryClassifier.Review(operation, false, backup, false);

        Assert.Equal(ReplacementRecoveryAction.ManualReview, decision.Action);
    }

    private static ReplacementOperation CreateOperation(
        ReplacementOperationStatus status,
        ReplacementVerificationStatus verificationStatus,
        string? failureMessage = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new ReplacementOperation(
            Guid.NewGuid(), Guid.NewGuid(), status, ReplacementOperationStage.BackingUpSource,
            @"C:\Videos\Source.mkv", @"D:\Converted\Output.mp4", @"C:\Videos\Source.mp4",
            @"C:\Videos\Source.mp4.hbcm-copying", @"C:\Videos\HandBrake Original Backup\Source.mkv",
            2_048, 1_024, 1_024, verificationStatus, failureMessage, now, now);
    }

    private static OriginalBackupState CreateBackup(ReplacementOperation operation, OriginalBackupStatus status) =>
        new(operation.Id, operation.BackupPath, status, operation.SourceSize, operation.SourceSize,
            status == OriginalBackupStatus.Verified ? "ABC" : null, null,
            operation.DateCreatedUtc, operation.DateUpdatedUtc);
}
