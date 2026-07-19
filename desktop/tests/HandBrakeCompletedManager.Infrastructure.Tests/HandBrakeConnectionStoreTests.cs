using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class HandBrakeConnectionStoreTests
{
    [Fact]
    public async Task SaveConnectedAsync_RoundTripsRepositoryConnection()
    {
        var testDirectory = CreateTestDirectory();
        var settingsPath = Path.Combine(testDirectory, "connections.json");
        var executablePath = Path.Combine(testDirectory, "HandBrake.exe");
        var testedAt = new DateTimeOffset(2026, 7, 20, 2, 3, 4, TimeSpan.Zero);

        try
        {
            var store = new HandBrakeConnectionStore(settingsPath);

            await store.SaveConnectedAsync(executablePath, testedAt);
            var connections = await store.LoadAsync();

            var connection = Assert.Single(connections);
            Assert.Equal(Path.GetFullPath(executablePath), connection.ExecutablePath);
            Assert.True(connection.IsConnected);
            Assert.Equal(testedAt, connection.LastTestedUtc);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_TreatsInvalidJsonAsNoSavedConnections()
    {
        var testDirectory = CreateTestDirectory();
        var settingsPath = Path.Combine(testDirectory, "connections.json");

        try
        {
            await File.WriteAllTextAsync(settingsPath, "not valid json");
            var store = new HandBrakeConnectionStore(settingsPath);

            var connections = await store.LoadAsync();

            Assert.Empty(connections);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "handbrake-completed-manager-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
