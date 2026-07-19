namespace HandBrakeCompletedManager.Core;

public sealed record CompletionEvent(
    string SourcePath,
    string DestinationPath,
    string DestinationFolder,
    int ExitCode,
    DateTimeOffset CompletedAtUtc);

