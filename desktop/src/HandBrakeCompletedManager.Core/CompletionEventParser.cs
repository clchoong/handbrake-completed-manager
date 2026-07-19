using System.Globalization;

namespace HandBrakeCompletedManager.Core;

public sealed record CompletionEventParseResult(CompletionEvent? Event, string? Error)
{
    public bool IsSuccess => Event is not null;

    public static CompletionEventParseResult Success(CompletionEvent completionEvent) =>
        new(completionEvent, null);

    public static CompletionEventParseResult Failure(string error) => new(null, error);
}

public static class CompletionEventParser
{
    public static CompletionEventParseResult Parse(
        IReadOnlyList<string> args,
        Func<string, string?> getEnvironmentVariable,
        DateTimeOffset? completedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var options = ParseOptions(args);
        var sourcePath = FindValue(options, "source", "hb-source")
            ?? getEnvironmentVariable("HB_SOURCE");
        var destinationPath = FindValue(options, "destination", "hb-destination")
            ?? getEnvironmentVariable("HB_DESTINATION");
        var destinationFolder = FindValue(options, "destination-folder", "hb-destination-folder")
            ?? getEnvironmentVariable("HB_DESTINATION_FOLDER");
        var exitCodeText = FindValue(options, "exit-code", "hb-exit-code")
            ?? getEnvironmentVariable("HB_EXIT_CODE");

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return CompletionEventParseResult.Failure(
                "A source path is required through --source or HB_SOURCE.");
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return CompletionEventParseResult.Failure(
                "A destination path is required through --destination or HB_DESTINATION.");
        }

        var exitCode = 0;
        if (!string.IsNullOrWhiteSpace(exitCodeText) &&
            !int.TryParse(exitCodeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out exitCode))
        {
            return CompletionEventParseResult.Failure("HB_EXIT_CODE must be a valid integer.");
        }

        sourcePath = sourcePath.Trim();
        destinationPath = destinationPath.Trim();
        destinationFolder = string.IsNullOrWhiteSpace(destinationFolder)
            ? Path.GetDirectoryName(destinationPath) ?? string.Empty
            : destinationFolder.Trim();

        return CompletionEventParseResult.Success(new CompletionEvent(
            sourcePath,
            destinationPath,
            destinationFolder,
            exitCode,
            completedAtUtc ?? DateTimeOffset.UtcNow));
    }

    private static Dictionary<string, string> ParseOptions(IReadOnlyList<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var option = argument[2..];
            var equalsIndex = option.IndexOf('=');
            if (equalsIndex >= 0)
            {
                options[option[..equalsIndex]] = option[(equalsIndex + 1)..];
                continue;
            }

            if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[option] = args[++index];
            }
        }

        return options;
    }

    private static string? FindValue(
        IReadOnlyDictionary<string, string> options,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (options.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        return null;
    }
}

