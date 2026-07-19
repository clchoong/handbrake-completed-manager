namespace HandBrakeCompletedManager.Core;

public sealed record ApplicationSettings(
    bool StartMinimized,
    bool CloseToTray,
    bool NotificationsEnabled,
    int HistoryRefreshSeconds)
{
    public static ApplicationSettings Default { get; } = new(
        StartMinimized: false,
        CloseToTray: true,
        NotificationsEnabled: true,
        HistoryRefreshSeconds: 3);

    public static IReadOnlyList<int> AllowedRefreshIntervals { get; } = [3, 5, 10, 30, 60];

    public ApplicationSettings Normalize() => this with
    {
        HistoryRefreshSeconds = AllowedRefreshIntervals.Contains(HistoryRefreshSeconds)
            ? HistoryRefreshSeconds
            : Default.HistoryRefreshSeconds
    };
}
