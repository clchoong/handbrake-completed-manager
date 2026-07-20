using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class FinalizationTransactionTests
{
    [Theory]
    [InlineData(FinalizationCheckpoint.Prepared, FinalizationCheckpoint.PromoteTemporaryIntentRecorded)]
    [InlineData(FinalizationCheckpoint.PromoteTemporaryIntentRecorded, FinalizationCheckpoint.FinalPromoted)]
    [InlineData(FinalizationCheckpoint.FinalPromoted, FinalizationCheckpoint.RecycleSourceIntentRecorded)]
    [InlineData(FinalizationCheckpoint.RecycleSourceIntentRecorded, FinalizationCheckpoint.SourceRecycled)]
    [InlineData(FinalizationCheckpoint.SourceRecycled, FinalizationCheckpoint.Completed)]
    [InlineData(FinalizationCheckpoint.Completed, FinalizationCheckpoint.UndoPrepared)]
    [InlineData(FinalizationCheckpoint.UndoPrepared, FinalizationCheckpoint.RestoreSourceIntentRecorded)]
    [InlineData(FinalizationCheckpoint.RestoreSourceIntentRecorded, FinalizationCheckpoint.SourceRestored)]
    [InlineData(FinalizationCheckpoint.SourceRestored, FinalizationCheckpoint.RecycleFinalIntentRecorded)]
    [InlineData(FinalizationCheckpoint.RecycleFinalIntentRecorded, FinalizationCheckpoint.FinalRecycled)]
    [InlineData(FinalizationCheckpoint.FinalRecycled, FinalizationCheckpoint.Undone)]
    public void StateMachine_AllowsDesignedForwardTransitions(
        FinalizationCheckpoint current,
        FinalizationCheckpoint next)
    {
        Assert.True(FinalizationStateMachine.CanTransition(current, next));
        FinalizationStateMachine.EnsureTransition(current, next);
    }

    [Theory]
    [InlineData(FinalizationCheckpoint.Prepared, FinalizationCheckpoint.Completed)]
    [InlineData(FinalizationCheckpoint.FinalPromoted, FinalizationCheckpoint.SourceRecycled)]
    [InlineData(FinalizationCheckpoint.Completed, FinalizationCheckpoint.Prepared)]
    [InlineData(FinalizationCheckpoint.Undone, FinalizationCheckpoint.Prepared)]
    public void StateMachine_RejectsSkippedOrTerminalTransitions(
        FinalizationCheckpoint current,
        FinalizationCheckpoint next)
    {
        Assert.False(FinalizationStateMachine.CanTransition(current, next));
        Assert.Throws<InvalidOperationException>(() => FinalizationStateMachine.EnsureTransition(current, next));
    }

    [Fact]
    public void RecoveryAdvisor_PreparedStateCanOnlyBeginWithAllVerifiedArtifacts()
    {
        var decision = Review(FinalizationCheckpoint.Prepared, source: true, temporary: true, final: false);

        Assert.True(decision.IsConsistent);
        Assert.Equal(FinalizationRecoveryAction.BeginPromotion, decision.Action);
    }

    [Theory]
    [InlineData(true, false, FinalizationRecoveryAction.RetryPromotion, FinalizationCheckpoint.PromoteTemporaryIntentRecorded)]
    [InlineData(false, true, FinalizationRecoveryAction.RecordFinalPromoted, FinalizationCheckpoint.FinalPromoted)]
    public void RecoveryAdvisor_ResolvesBothPromotionCrashBoundaries(
        bool temporary,
        bool final,
        FinalizationRecoveryAction action,
        FinalizationCheckpoint checkpoint)
    {
        var decision = Review(FinalizationCheckpoint.PromoteTemporaryIntentRecorded, true, temporary, final);

        Assert.True(decision.IsConsistent);
        Assert.Equal(action, decision.Action);
        Assert.Equal(checkpoint, decision.RecommendedCheckpoint);
    }

    [Theory]
    [InlineData(true, FinalizationRecoveryAction.RetrySourceRecycle, FinalizationCheckpoint.RecycleSourceIntentRecorded)]
    [InlineData(false, FinalizationRecoveryAction.RecordSourceRecycled, FinalizationCheckpoint.SourceRecycled)]
    public void RecoveryAdvisor_ResolvesBothSourceRecycleCrashBoundaries(
        bool source,
        FinalizationRecoveryAction action,
        FinalizationCheckpoint checkpoint)
    {
        var decision = Review(FinalizationCheckpoint.RecycleSourceIntentRecorded, source, false, true);

        Assert.True(decision.IsConsistent);
        Assert.Equal(action, decision.Action);
        Assert.Equal(checkpoint, decision.RecommendedCheckpoint);
    }

    [Theory]
    [InlineData(false, FinalizationRecoveryAction.RetrySourceRestore, FinalizationCheckpoint.RestoreSourceIntentRecorded)]
    [InlineData(true, FinalizationRecoveryAction.RecordSourceRestored, FinalizationCheckpoint.SourceRestored)]
    public void RecoveryAdvisor_ResolvesBothSourceRestoreCrashBoundaries(
        bool source,
        FinalizationRecoveryAction action,
        FinalizationCheckpoint checkpoint)
    {
        var decision = Review(FinalizationCheckpoint.RestoreSourceIntentRecorded, source, false, true);

        Assert.True(decision.IsConsistent);
        Assert.Equal(action, decision.Action);
        Assert.Equal(checkpoint, decision.RecommendedCheckpoint);
    }

    [Theory]
    [InlineData(true, FinalizationRecoveryAction.RetryFinalRecycle, FinalizationCheckpoint.RecycleFinalIntentRecorded)]
    [InlineData(false, FinalizationRecoveryAction.RecordFinalRecycled, FinalizationCheckpoint.FinalRecycled)]
    public void RecoveryAdvisor_ResolvesBothFinalRecycleCrashBoundaries(
        bool final,
        FinalizationRecoveryAction action,
        FinalizationCheckpoint checkpoint)
    {
        var decision = Review(FinalizationCheckpoint.RecycleFinalIntentRecorded, true, false, final);

        Assert.True(decision.IsConsistent);
        Assert.Equal(action, decision.Action);
        Assert.Equal(checkpoint, decision.RecommendedCheckpoint);
    }

    [Fact]
    public void RecoveryAdvisor_RequiresManualReviewWhenBackupDoesNotMatch()
    {
        var transaction = CreateTransaction(FinalizationCheckpoint.Prepared);
        var artifacts = Snapshot(true, true, false) with
        {
            Backup = new FinalizationArtifactObservation(true, "BAD")
        };

        var decision = FinalizationRecoveryAdvisor.Review(transaction, artifacts);

        Assert.False(decision.IsConsistent);
        Assert.Equal(FinalizationRecoveryAction.ManualReview, decision.Action);
    }

    [Fact]
    public void RecoveryAdvisor_RequiresManualReviewForAmbiguousDuplicateFinalFile()
    {
        var decision = Review(FinalizationCheckpoint.PromoteTemporaryIntentRecorded, true, true, true);

        Assert.False(decision.IsConsistent);
        Assert.Equal(FinalizationRecoveryAction.ManualReview, decision.Action);
    }

    [Fact]
    public void RecoveryAdvisor_DoesNotAcceptMissingSourceBeforeRecycleIntent()
    {
        var decision = Review(FinalizationCheckpoint.FinalPromoted, source: false, temporary: false, final: true);

        Assert.False(decision.IsConsistent);
        Assert.Equal(FinalizationRecoveryAction.ManualReview, decision.Action);
    }

    private static FinalizationRecoveryDecision Review(
        FinalizationCheckpoint checkpoint,
        bool source,
        bool temporary,
        bool final) =>
        FinalizationRecoveryAdvisor.Review(CreateTransaction(checkpoint), Snapshot(source, temporary, final));

    private static FinalizationTransaction CreateTransaction(FinalizationCheckpoint checkpoint)
    {
        var now = DateTimeOffset.UtcNow;
        return new FinalizationTransaction(Guid.NewGuid(), checkpoint, SourceHash, FinalHash, 0, null, now, now);
    }

    private static FinalizationArtifactSnapshot Snapshot(bool source, bool temporary, bool final) => new(
        Observation(source, SourceHash),
        Observation(temporary, FinalHash),
        Observation(final, FinalHash),
        Observation(true, SourceHash));

    private static FinalizationArtifactObservation Observation(bool exists, string hash) =>
        new(exists, exists ? hash : null);

    private const string SourceHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string FinalHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
}
