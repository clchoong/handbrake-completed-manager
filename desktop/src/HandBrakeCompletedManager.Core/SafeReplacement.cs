namespace HandBrakeCompletedManager.Core;

public enum SafeReplacementStage
{
    CopyingConvertedFile,
    BackingUpOriginalSource,
    VerifyingAllArtifacts,
    PromotingConvertedFile,
    RecyclingOriginalSource,
    CompletingTransaction
}

public sealed record SafeReplacementProgress(
    SafeReplacementStage Stage,
    string Message,
    double? Percentage = null);

public sealed record SafeReplacementResult(
    Guid OperationId,
    string FinalPath,
    string BackupPath);
