namespace HandBrakeCompletedManager.Core;

public enum SourceReplacementState
{
    NotReplaced,
    InProgress,
    NeedsAttention,
    Replaced,
    Restored
}

public sealed record OutputRecycleResult(
    Guid CompletedEncodeId,
    string OutputPath);
