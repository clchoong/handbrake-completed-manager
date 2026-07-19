using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public static class CompletedEncodeCapture
{
    public static CompletedEncode Create(CompletionEvent completionEvent)
    {
        var source = ReadFile(completionEvent.SourcePath);
        var destination = ReadFile(completionEvent.DestinationPath);
        var metrics = EncodeSizeMetrics.Calculate(source.Size, destination.Size);
        var now = DateTimeOffset.UtcNow;
        var fingerprint = CompletionFingerprint.Create(
            completionEvent.SourcePath,
            completionEvent.DestinationPath,
            source.Size,
            destination.Size,
            destination.LastWriteUtc,
            completionEvent.CompletedAtUtc);

        return new CompletedEncode(
            Guid.NewGuid(),
            fingerprint,
            completionEvent.CompletedAtUtc.ToUniversalTime(),
            completionEvent.SourcePath,
            Path.GetFileName(completionEvent.SourcePath),
            Path.GetExtension(completionEvent.SourcePath),
            source.Size,
            source.Exists,
            completionEvent.DestinationPath,
            Path.GetFileName(completionEvent.DestinationPath),
            Path.GetExtension(completionEvent.DestinationPath),
            destination.Size,
            destination.Exists,
            destination.LastWriteUtc,
            metrics.OutputPercentage,
            metrics.SpaceSavedPercentage,
            metrics.SpaceSavedBytes,
            completionEvent.ExitCode,
            DetermineStatus(completionEvent.ExitCode, source.Exists, destination.Exists),
            now,
            now);
    }

    private static FileSnapshot ReadFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists
                ? new FileSnapshot(true, file.Length, file.LastWriteTimeUtc)
                : new FileSnapshot(false, null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new FileSnapshot(false, null, null);
        }
    }

    private static string DetermineStatus(int exitCode, bool sourceExists, bool destinationExists)
    {
        if (exitCode != 0)
        {
            return "Encode Failed";
        }

        if (!sourceExists)
        {
            return "Source Missing";
        }

        if (!destinationExists)
        {
            return "Converted File Missing";
        }

        return "Completed";
    }

    private sealed record FileSnapshot(bool Exists, long? Size, DateTimeOffset? LastWriteUtc);
}
