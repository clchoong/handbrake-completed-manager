namespace HandBrakeCompletedManager.Core;

public enum CompletedEncodeQuickFilter
{
    All,
    Today,
    LastSevenDays,
    MissingFiles,
    OutputLargerThanSource
}

public static class CompletedEncodeFilter
{
    public static bool Matches(
        CompletedEncode item,
        string? searchText,
        CompletedEncodeQuickFilter quickFilter,
        DateTimeOffset now,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        return MatchesSearch(item, searchText) &&
               MatchesQuickFilter(item, quickFilter, now, timeZone ?? TimeZoneInfo.Local);
    }

    private static bool MatchesSearch(CompletedEncode item, string? searchText)
    {
        var terms = (searchText ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (terms.Length == 0)
        {
            return true;
        }

        string[] searchableValues =
        [
            item.SourceFilename,
            item.SourcePath,
            item.DestinationFilename,
            item.DestinationPath,
            item.CurrentStatus,
            item.SourceExtension,
            item.DestinationExtension
        ];

        return terms.All(term => searchableValues.Any(value =>
            value.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool MatchesQuickFilter(
        CompletedEncode item,
        CompletedEncodeQuickFilter quickFilter,
        DateTimeOffset now,
        TimeZoneInfo timeZone) => quickFilter switch
    {
        CompletedEncodeQuickFilter.All => true,
        CompletedEncodeQuickFilter.Today =>
            TimeZoneInfo.ConvertTime(item.CompletedAtUtc, timeZone).Date ==
            TimeZoneInfo.ConvertTime(now, timeZone).Date,
        CompletedEncodeQuickFilter.LastSevenDays =>
            item.CompletedAtUtc >= now.ToUniversalTime().AddDays(-7) &&
            item.CompletedAtUtc <= now.ToUniversalTime(),
        CompletedEncodeQuickFilter.MissingFiles => !item.SourceExists || !item.DestinationExists,
        CompletedEncodeQuickFilter.OutputLargerThanSource =>
            item.SourceSize is not null &&
            item.DestinationSize is not null &&
            item.DestinationSize > item.SourceSize,
        _ => throw new ArgumentOutOfRangeException(nameof(quickFilter), quickFilter, null)
    };
}
