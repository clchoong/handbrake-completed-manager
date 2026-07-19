namespace HandBrakeCompletedManager.Core;

public sealed record CompletedEncode(
    Guid Id,
    string EventFingerprint,
    DateTimeOffset CompletedAtUtc,
    string SourcePath,
    string SourceFilename,
    string SourceExtension,
    long? SourceSize,
    bool SourceExists,
    string DestinationPath,
    string DestinationFilename,
    string DestinationExtension,
    long? DestinationSize,
    bool DestinationExists,
    DateTimeOffset? DestinationLastWriteUtc,
    double? OutputPercentage,
    double? SpaceSavedPercentage,
    long? SpaceSavedBytes,
    int ExitCode,
    string CurrentStatus,
    DateTimeOffset DateCreatedUtc,
    DateTimeOffset DateUpdatedUtc);

