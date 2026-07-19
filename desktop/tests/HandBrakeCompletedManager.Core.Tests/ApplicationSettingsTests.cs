using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class ApplicationSettingsTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void Normalize_PreservesAllowedRefreshInterval(int interval)
    {
        var settings = ApplicationSettings.Default with { HistoryRefreshSeconds = interval };

        Assert.Equal(interval, settings.Normalize().HistoryRefreshSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(3600)]
    public void Normalize_ReplacesUnsupportedRefreshInterval(int interval)
    {
        var settings = ApplicationSettings.Default with { HistoryRefreshSeconds = interval };

        Assert.Equal(ApplicationSettings.Default.HistoryRefreshSeconds, settings.Normalize().HistoryRefreshSeconds);
    }
}
