namespace HandBrakeCompletedManager.Core;

public enum FinalizationCheckpoint
{
    Prepared,
    PromoteTemporaryIntentRecorded,
    FinalPromoted,
    RecycleSourceIntentRecorded,
    SourceRecycled,
    Completed,
    RecoveryRequired,
    UndoPrepared,
    RestoreSourceIntentRecorded,
    SourceRestored,
    RecycleFinalIntentRecorded,
    FinalRecycled,
    Undone
}

public sealed record FinalizationTransaction(
    Guid OperationId,
    FinalizationCheckpoint Checkpoint,
    string SourceSha256,
    string FinalSha256,
    int Revision,
    string? FailureMessage,
    DateTimeOffset DateCreatedUtc,
    DateTimeOffset DateUpdatedUtc);

public sealed record FinalizationPromotionResult(
    Guid OperationId,
    string FinalPath,
    string Sha256,
    bool WasRecovered);

public sealed record SourceRestorationResult(
    Guid OperationId,
    string SourcePath,
    string Sha256,
    bool WasRecovered,
    bool WasResumed);

public sealed record SourceRecycleResult(
    Guid OperationId,
    string SourcePath,
    bool WasRecovered);

public sealed record FinalizationCompletionResult(
    Guid OperationId,
    string FinalPath,
    bool WasAlreadyCompleted);

public static class FinalizationStateMachine
{
    private static readonly IReadOnlyDictionary<FinalizationCheckpoint, IReadOnlySet<FinalizationCheckpoint>> Transitions =
        new Dictionary<FinalizationCheckpoint, IReadOnlySet<FinalizationCheckpoint>>
        {
            [FinalizationCheckpoint.Prepared] = Set(FinalizationCheckpoint.PromoteTemporaryIntentRecorded),
            [FinalizationCheckpoint.PromoteTemporaryIntentRecorded] = Set(FinalizationCheckpoint.FinalPromoted, FinalizationCheckpoint.RecoveryRequired),
            [FinalizationCheckpoint.FinalPromoted] = Set(FinalizationCheckpoint.RecycleSourceIntentRecorded, FinalizationCheckpoint.UndoPrepared, FinalizationCheckpoint.RecoveryRequired),
            [FinalizationCheckpoint.RecycleSourceIntentRecorded] = Set(FinalizationCheckpoint.SourceRecycled, FinalizationCheckpoint.RecoveryRequired),
            [FinalizationCheckpoint.SourceRecycled] = Set(FinalizationCheckpoint.Completed, FinalizationCheckpoint.UndoPrepared, FinalizationCheckpoint.RecoveryRequired),
            [FinalizationCheckpoint.Completed] = Set(FinalizationCheckpoint.UndoPrepared),
            [FinalizationCheckpoint.RecoveryRequired] = Set(
                FinalizationCheckpoint.PromoteTemporaryIntentRecorded,
                FinalizationCheckpoint.FinalPromoted,
                FinalizationCheckpoint.RecycleSourceIntentRecorded,
                FinalizationCheckpoint.SourceRecycled,
                FinalizationCheckpoint.UndoPrepared,
                FinalizationCheckpoint.RestoreSourceIntentRecorded,
                FinalizationCheckpoint.SourceRestored,
                FinalizationCheckpoint.RecycleFinalIntentRecorded,
                FinalizationCheckpoint.FinalRecycled),
            [FinalizationCheckpoint.UndoPrepared] = Set(FinalizationCheckpoint.RestoreSourceIntentRecorded, FinalizationCheckpoint.RecycleFinalIntentRecorded),
            [FinalizationCheckpoint.RestoreSourceIntentRecorded] = Set(FinalizationCheckpoint.SourceRestored, FinalizationCheckpoint.RecoveryRequired),
            [FinalizationCheckpoint.SourceRestored] = Set(FinalizationCheckpoint.RecycleFinalIntentRecorded, FinalizationCheckpoint.RecoveryRequired),
            [FinalizationCheckpoint.RecycleFinalIntentRecorded] = Set(FinalizationCheckpoint.FinalRecycled, FinalizationCheckpoint.RecoveryRequired),
            [FinalizationCheckpoint.FinalRecycled] = Set(FinalizationCheckpoint.Undone, FinalizationCheckpoint.RecoveryRequired),
            [FinalizationCheckpoint.Undone] = Set()
        };

    public static bool CanTransition(FinalizationCheckpoint current, FinalizationCheckpoint next) =>
        Transitions.TryGetValue(current, out var allowed) && allowed.Contains(next);

    public static void EnsureTransition(FinalizationCheckpoint current, FinalizationCheckpoint next)
    {
        if (!CanTransition(current, next))
        {
            throw new InvalidOperationException($"Finalisation checkpoint cannot transition from {current} to {next}.");
        }
    }

    private static IReadOnlySet<FinalizationCheckpoint> Set(params FinalizationCheckpoint[] values) =>
        new HashSet<FinalizationCheckpoint>(values);
}

public sealed record FinalizationArtifactObservation(bool Exists, string? Sha256 = null);

public sealed record FinalizationArtifactSnapshot(
    FinalizationArtifactObservation Source,
    FinalizationArtifactObservation Temporary,
    FinalizationArtifactObservation Final,
    FinalizationArtifactObservation Backup);

public enum FinalizationRecoveryAction
{
    None,
    BeginPromotion,
    RetryPromotion,
    RecordFinalPromoted,
    BeginSourceRecycle,
    RetrySourceRecycle,
    RecordSourceRecycled,
    CompleteFinalization,
    BeginUndo,
    BeginSourceRestore,
    RetrySourceRestore,
    RecordSourceRestored,
    BeginFinalRecycle,
    RetryFinalRecycle,
    RecordFinalRecycled,
    CompleteUndo,
    ManualReview
}

public sealed record FinalizationRecoveryDecision(
    bool IsConsistent,
    FinalizationRecoveryAction Action,
    FinalizationCheckpoint? RecommendedCheckpoint,
    string Message);

public static class FinalizationRecoveryAdvisor
{
    public static FinalizationRecoveryDecision Review(
        FinalizationTransaction transaction,
        FinalizationArtifactSnapshot artifacts)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(artifacts);
        if (!Matches(artifacts.Backup, transaction.SourceSha256))
        {
            return Manual("The verified original backup is missing or does not match the prepared source digest.");
        }

        var source = Matches(artifacts.Source, transaction.SourceSha256);
        var temporary = Matches(artifacts.Temporary, transaction.FinalSha256);
        var final = Matches(artifacts.Final, transaction.FinalSha256);
        return transaction.Checkpoint switch
        {
            FinalizationCheckpoint.Prepared when source && temporary && !artifacts.Final.Exists =>
                Safe(FinalizationRecoveryAction.BeginPromotion, FinalizationCheckpoint.PromoteTemporaryIntentRecorded,
                    "The prepared transaction is consistent; promotion could begin after explicit confirmation."),
            FinalizationCheckpoint.PromoteTemporaryIntentRecorded when source && temporary && !artifacts.Final.Exists =>
                Safe(FinalizationRecoveryAction.RetryPromotion, FinalizationCheckpoint.PromoteTemporaryIntentRecorded,
                    "Promotion did not occur and could be retried."),
            FinalizationCheckpoint.PromoteTemporaryIntentRecorded when source && !artifacts.Temporary.Exists && final =>
                Safe(FinalizationRecoveryAction.RecordFinalPromoted, FinalizationCheckpoint.FinalPromoted,
                    "The atomic promotion occurred before interruption; its completed checkpoint can be recorded."),
            FinalizationCheckpoint.FinalPromoted when final && !artifacts.Temporary.Exists && source =>
                Safe(FinalizationRecoveryAction.BeginSourceRecycle, FinalizationCheckpoint.RecycleSourceIntentRecorded,
                    "The final file is verified and the source is still present; source recycling could be prepared."),
            FinalizationCheckpoint.RecycleSourceIntentRecorded when final && !artifacts.Temporary.Exists && source =>
                Safe(FinalizationRecoveryAction.RetrySourceRecycle, FinalizationCheckpoint.RecycleSourceIntentRecorded,
                    "Source recycling did not occur and could be retried."),
            FinalizationCheckpoint.RecycleSourceIntentRecorded when final && !artifacts.Temporary.Exists && !artifacts.Source.Exists =>
                Safe(FinalizationRecoveryAction.RecordSourceRecycled, FinalizationCheckpoint.SourceRecycled,
                    "Source recycling occurred before interruption; its completed checkpoint can be recorded."),
            FinalizationCheckpoint.SourceRecycled when final && !artifacts.Temporary.Exists && !artifacts.Source.Exists =>
                Safe(FinalizationRecoveryAction.CompleteFinalization, FinalizationCheckpoint.Completed,
                    "All finalisation artifacts match and the transaction can be marked complete."),
            FinalizationCheckpoint.Completed when final && !artifacts.Temporary.Exists && !artifacts.Source.Exists =>
                Safe(FinalizationRecoveryAction.BeginUndo, FinalizationCheckpoint.UndoPrepared,
                    "The completed transaction is consistent; a separately confirmed undo could be prepared."),
            FinalizationCheckpoint.UndoPrepared when !artifacts.Source.Exists && final =>
                Safe(FinalizationRecoveryAction.BeginSourceRestore, FinalizationCheckpoint.RestoreSourceIntentRecorded,
                    "Undo can restore the verified source before touching the final file."),
            FinalizationCheckpoint.UndoPrepared when source && final =>
                Safe(FinalizationRecoveryAction.BeginFinalRecycle, FinalizationCheckpoint.RecycleFinalIntentRecorded,
                    "The source is already restored; final-file recycling could be prepared."),
            FinalizationCheckpoint.RestoreSourceIntentRecorded when !artifacts.Source.Exists && final =>
                Safe(FinalizationRecoveryAction.RetrySourceRestore, FinalizationCheckpoint.RestoreSourceIntentRecorded,
                    "Source restoration did not occur and could be retried from the verified backup."),
            FinalizationCheckpoint.RestoreSourceIntentRecorded when source && final =>
                Safe(FinalizationRecoveryAction.RecordSourceRestored, FinalizationCheckpoint.SourceRestored,
                    "Source restoration occurred before interruption; its completed checkpoint can be recorded."),
            FinalizationCheckpoint.SourceRestored when source && final =>
                Safe(FinalizationRecoveryAction.BeginFinalRecycle, FinalizationCheckpoint.RecycleFinalIntentRecorded,
                    "The source is verified at its original path; final-file recycling could be prepared."),
            FinalizationCheckpoint.RecycleFinalIntentRecorded when source && final =>
                Safe(FinalizationRecoveryAction.RetryFinalRecycle, FinalizationCheckpoint.RecycleFinalIntentRecorded,
                    "Final-file recycling did not occur and could be retried."),
            FinalizationCheckpoint.RecycleFinalIntentRecorded when source && !artifacts.Final.Exists =>
                Safe(FinalizationRecoveryAction.RecordFinalRecycled, FinalizationCheckpoint.FinalRecycled,
                    "Final-file recycling occurred before interruption; its completed checkpoint can be recorded."),
            FinalizationCheckpoint.FinalRecycled when source && !artifacts.Final.Exists =>
                Safe(FinalizationRecoveryAction.CompleteUndo, FinalizationCheckpoint.Undone,
                    "The restored source is verified and undo can be marked complete."),
            FinalizationCheckpoint.Undone when source && !artifacts.Final.Exists =>
                Safe(FinalizationRecoveryAction.None, null, "The undone transaction is consistent."),
            FinalizationCheckpoint.RecoveryRequired =>
                Manual("The transaction is already marked for manual recovery and requires an explicit checkpoint decision."),
            _ => Manual("The current files do not match the transaction checkpoint; automatic continuation is unsafe.")
        };
    }

    private static bool Matches(FinalizationArtifactObservation artifact, string expectedSha256) =>
        artifact.Exists && string.Equals(artifact.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase);

    private static FinalizationRecoveryDecision Safe(
        FinalizationRecoveryAction action,
        FinalizationCheckpoint? checkpoint,
        string message) => new(true, action, checkpoint, message);

    private static FinalizationRecoveryDecision Manual(string message) =>
        new(false, FinalizationRecoveryAction.ManualReview, FinalizationCheckpoint.RecoveryRequired, message);
}
