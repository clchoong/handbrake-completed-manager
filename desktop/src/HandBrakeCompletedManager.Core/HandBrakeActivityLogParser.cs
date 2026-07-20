using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HandBrakeCompletedManager.Core;

public enum HandBrakeActivityLogStatus
{
    Completed,
    Incomplete,
    Failed,
    Invalid
}

public sealed record HandBrakeActivityLogParseResult(
    HandBrakeActivityLogStatus Status,
    CompletionEvent? CompletionEvent,
    string Message)
{
    public bool IsRecoverable => Status == HandBrakeActivityLogStatus.Completed && CompletionEvent is not null;
}

public static partial class HandBrakeActivityLogParser
{
    private static readonly string[] FinishedAtFormats =
    [
        "ddd MMM d HH:mm:ss yyyy",
        "ddd MMM dd HH:mm:ss yyyy"
    ];

    public static HandBrakeActivityLogParseResult Parse(
        string content,
        DateTimeOffset fallbackCompletedAt)
    {
        ArgumentNullException.ThrowIfNull(content);

        var resultMatch = WorkResultRegex().Match(content);
        var hasCompletedMarker = JobCompletedRegex().IsMatch(content);
        if (!resultMatch.Success || !hasCompletedMarker)
        {
            return new HandBrakeActivityLogParseResult(
                HandBrakeActivityLogStatus.Incomplete,
                null,
                "Incomplete log — no successful completion marker");
        }

        if (!int.TryParse(resultMatch.Groups["code"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode))
        {
            return new HandBrakeActivityLogParseResult(
                HandBrakeActivityLogStatus.Invalid,
                null,
                "Invalid log — unreadable HandBrake result code");
        }

        if (exitCode != 0)
        {
            return new HandBrakeActivityLogParseResult(
                HandBrakeActivityLogStatus.Failed,
                null,
                $"Failed encode — HandBrake result {exitCode}");
        }

        var sourcePath = ReadJsonPath(SourcePathRegex(), content);
        var destinationPath = ReadJsonPath(DestinationPathRegex(), content);
        if (!IsUsableAbsolutePath(sourcePath) || !IsUsableAbsolutePath(destinationPath))
        {
            return new HandBrakeActivityLogParseResult(
                HandBrakeActivityLogStatus.Invalid,
                null,
                "Invalid log — source or output path is missing");
        }

        var completedAt = ReadCompletedAt(content) ?? fallbackCompletedAt;
        var completionEvent = new CompletionEvent(
            Path.GetFullPath(sourcePath!),
            Path.GetFullPath(destinationPath!),
            Path.GetDirectoryName(destinationPath!) ?? string.Empty,
            exitCode,
            completedAt.ToUniversalTime());
        return new HandBrakeActivityLogParseResult(
            HandBrakeActivityLogStatus.Completed,
            completionEvent,
            "Recoverable completed encode");
    }

    private static string? ReadJsonPath(Regex regex, string content)
    {
        var match = regex.Match(content);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>($"\"{match.Groups["path"].Value}\"");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ReadCompletedAt(string content)
    {
        var match = FinishedAtRegex().Match(content);
        if (!match.Success ||
            !DateTime.TryParseExact(
                match.Groups["date"].Value.Trim(),
                FinishedAtFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var localTime))
        {
            return null;
        }

        localTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
    }

    private static bool IsUsableAbsolutePath(string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    [GeneratedRegex("libhb:\\s*work result\\s*=\\s*(?<code>-?\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WorkResultRegex();

    [GeneratedRegex("#\\s*Job Completed!", RegexOptions.IgnoreCase)]
    private static partial Regex JobCompletedRegex();

    [GeneratedRegex("Finished work at:\\s*(?<date>[^\\r\\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FinishedAtRegex();

    [GeneratedRegex("\"Destination\"\\s*:\\s*\\{.*?\"File\"\\s*:\\s*\"(?<path>(?:\\\\.|[^\"])*)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex DestinationPathRegex();

    [GeneratedRegex("\"Source\"\\s*:\\s*\\{.*?\"Path\"\\s*:\\s*\"(?<path>(?:\\\\.|[^\"])*)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SourcePathRegex();
}
