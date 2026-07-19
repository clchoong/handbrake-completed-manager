using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class ApplicationSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_ReturnsDefaultsWhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new ApplicationSettingsStore(path);

        var settings = await store.LoadAsync();

        Assert.Equal(ApplicationSettings.Default, settings);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsNormalizedSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hbcm-settings-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        var store = new ApplicationSettingsStore(path);

        try
        {
            await store.SaveAsync(new ApplicationSettings(true, false, false, 30));

            var settings = await store.LoadAsync();

            Assert.Equal(new ApplicationSettings(true, false, false, 30), settings);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaultsForInvalidJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hbcm-settings-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "not-json");
        var store = new ApplicationSettingsStore(path);

        try
        {
            Assert.Equal(ApplicationSettings.Default, await store.LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
