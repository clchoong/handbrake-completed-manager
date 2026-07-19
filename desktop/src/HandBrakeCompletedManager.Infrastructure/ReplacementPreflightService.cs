using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class ReplacementPreflightService
{
    public ReplacementPlan Review(CompletedEncode completedEncode)
    {
        ArgumentNullException.ThrowIfNull(completedEncode);
        var paths = ReplacementPlanner.BuildPaths(completedEncode);
        var source = ReadFile(completedEncode.SourcePath);
        var destination = ReadFile(completedEncode.DestinationPath);
        var snapshot = new ReplacementPreflightSnapshot(
            source.Exists,
            source.Size,
            destination.Exists,
            destination.Size,
            PathExists(paths.FinalPath),
            PathExists(paths.TemporaryPath),
            destination.LastWriteUtc);
        return ReplacementPlanner.Create(completedEncode, snapshot);
    }

    private static (bool Exists, long? Size, DateTimeOffset? LastWriteUtc) ReadFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists
                ? (true, file.Length, file.LastWriteTimeUtc)
                : (false, null, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return (File.Exists(path), null, null);
        }
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);
}
