using System.Security.Cryptography;
using System.Text;

namespace HandBrakeCompletedManager.Core;

public static class CompletionFingerprint
{
    public static string Create(
        string sourcePath,
        string destinationPath,
        long? sourceSize,
        long? destinationSize,
        DateTimeOffset? destinationLastWriteUtc,
        DateTimeOffset completedAtUtc)
    {
        var timeIdentity = destinationLastWriteUtc?.UtcTicks
            ?? completedAtUtc.ToUniversalTime().Ticks / TimeSpan.TicksPerMinute;

        var identity = string.Join('\n',
            NormalizePath(sourcePath),
            NormalizePath(destinationPath),
            sourceSize?.ToString() ?? "missing",
            destinationSize?.ToString() ?? "missing",
            timeIdentity.ToString());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        try
        {
            trimmed = Path.GetFullPath(trimmed);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Retain the supplied path for identity generation when Windows cannot normalize it.
        }

        return trimmed.ToUpperInvariant();
    }
}

