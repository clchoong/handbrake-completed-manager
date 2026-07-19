namespace HandBrakeCompletedManager.Core;

public enum ReplacementIssueSeverity
{
    Warning,
    Blocking
}

public sealed record ReplacementIssue(
    string Code,
    ReplacementIssueSeverity Severity,
    string Message);

public sealed record ReplacementPaths(
    string FinalPath,
    string TemporaryPath,
    string BackupPath);

public sealed record ReplacementPreflightSnapshot(
    bool SourceExists,
    long? SourceSize,
    bool DestinationExists,
    long? DestinationSize,
    bool FinalPathExists,
    bool TemporaryPathExists);

public sealed record ReplacementPlan(
    CompletedEncode CompletedEncode,
    ReplacementPaths Paths,
    ReplacementPreflightSnapshot Snapshot,
    IReadOnlyList<ReplacementIssue> Issues)
{
    public bool CanProceed => Issues.All(issue => issue.Severity != ReplacementIssueSeverity.Blocking);
}

public static class ReplacementPlanner
{
    public const string TemporarySuffix = ".hbcm-copying";
    public const string BackupDirectoryName = "HandBrake Original Backup";

    public static ReplacementPaths BuildPaths(CompletedEncode completedEncode)
    {
        ArgumentNullException.ThrowIfNull(completedEncode);
        var sourcePath = Path.GetFullPath(completedEncode.SourcePath);
        var sourceDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new ArgumentException("The source path has no parent directory.", nameof(completedEncode));
        var destinationExtension = Path.GetExtension(completedEncode.DestinationPath);
        if (string.IsNullOrWhiteSpace(destinationExtension))
        {
            throw new ArgumentException("The converted file has no extension.", nameof(completedEncode));
        }

        var finalPath = Path.Combine(
            sourceDirectory,
            Path.GetFileNameWithoutExtension(sourcePath) + destinationExtension);
        return new ReplacementPaths(
            finalPath,
            finalPath + TemporarySuffix,
            Path.Combine(sourceDirectory, BackupDirectoryName, Path.GetFileName(sourcePath)));
    }

    public static ReplacementPlan Create(
        CompletedEncode completedEncode,
        ReplacementPreflightSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(completedEncode);
        ArgumentNullException.ThrowIfNull(snapshot);
        var paths = BuildPaths(completedEncode);
        var issues = new List<ReplacementIssue>();

        AddBlockingWhen(!snapshot.SourceExists, "SourceMissing", "The source file is missing.", issues);
        AddBlockingWhen(!snapshot.DestinationExists, "DestinationMissing", "The converted file is missing.", issues);
        AddBlockingWhen(snapshot.SourceSize is null or <= 0, "SourceEmpty", "The source file is empty or its size cannot be read.", issues);
        AddBlockingWhen(snapshot.DestinationSize is null or <= 0, "DestinationEmpty", "The converted file is empty or its size cannot be read.", issues);

        AddBlockingWhen(
            PathsEqual(completedEncode.SourcePath, completedEncode.DestinationPath),
            "SameFile",
            "The source and converted paths refer to the same file.",
            issues);
        AddBlockingWhen(
            string.Equals(
                Path.GetExtension(completedEncode.SourcePath),
                Path.GetExtension(completedEncode.DestinationPath),
                StringComparison.OrdinalIgnoreCase),
            "SameExtension",
            "Source replacement with the same file extension is not supported.",
            issues);
        AddBlockingWhen(
            snapshot.FinalPathExists,
            "FinalPathConflict",
            "A file or folder already exists at the planned final path.",
            issues);
        AddBlockingWhen(
            snapshot.TemporaryPathExists,
            "TemporaryPathConflict",
            "A temporary replacement path already exists and must be reviewed before continuing.",
            issues);

        AddBlockingWhen(
            snapshot.SourceExists &&
            completedEncode.SourceSize is not null &&
            snapshot.SourceSize != completedEncode.SourceSize,
            "SourceChanged",
            "The source size changed after the encode was recorded.",
            issues);
        AddBlockingWhen(
            snapshot.DestinationExists &&
            completedEncode.DestinationSize is not null &&
            snapshot.DestinationSize != completedEncode.DestinationSize,
            "DestinationChanged",
            "The converted size changed after the encode was recorded.",
            issues);

        if (snapshot.SourceSize > 0 && snapshot.DestinationSize > snapshot.SourceSize)
        {
            issues.Add(new ReplacementIssue(
                "OutputLargerThanSource",
                ReplacementIssueSeverity.Warning,
                "The converted file is larger than the source file."));
        }

        return new ReplacementPlan(completedEncode, paths, snapshot, issues);
    }

    private static void AddBlockingWhen(
        bool condition,
        string code,
        string message,
        ICollection<ReplacementIssue> issues)
    {
        if (condition)
        {
            issues.Add(new ReplacementIssue(code, ReplacementIssueSeverity.Blocking, message));
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
