namespace HandBrakeCompletedManager.Core;

public enum ReplacementOperationStatus
{
    Planned,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

public enum ReplacementOperationStage
{
    Preparing,
    Copying,
    Verifying,
    BackingUpSource,
    Finalizing,
    Completed,
    Failed,
    Cancelled
}

public enum ReplacementVerificationStatus
{
    NotVerified,
    Verified,
    Warning,
    Failed
}

public sealed record ReplacementOperation(
    Guid Id,
    Guid CompletedEncodeId,
    ReplacementOperationStatus Status,
    ReplacementOperationStage Stage,
    string SourcePath,
    string DestinationPath,
    string FinalPath,
    string TemporaryPath,
    string BackupPath,
    long SourceSize,
    long DestinationSize,
    long BytesCopied,
    ReplacementVerificationStatus VerificationStatus,
    string? FailureMessage,
    DateTimeOffset DateCreatedUtc,
    DateTimeOffset DateUpdatedUtc);
