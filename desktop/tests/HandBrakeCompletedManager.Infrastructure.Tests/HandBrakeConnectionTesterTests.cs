using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class HandBrakeConnectionTesterTests
{
    [Fact]
    public async Task TestAsync_PersistsSimulatedEventWithoutUsingRealHistory()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "handbrake-completed-manager-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var executablePath = Path.Combine(testDirectory, "HandBrake.exe");
        await File.WriteAllBytesAsync(executablePath, [0x4D, 0x5A]);
        var installation = new HandBrakeInstallation(
            executablePath,
            "Test",
            HandBrakeInstallationType.Portable,
            true,
            false,
            "Test");

        try
        {
            var tester = new HandBrakeConnectionTester();

            var result = await tester.TestAsync(installation);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains("reached SQLite", result.Message);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task TestAsync_RejectsMissingExecutable()
    {
        var installation = new HandBrakeInstallation(
            @"C:\Missing\HandBrake.exe",
            null,
            HandBrakeInstallationType.Portable,
            false,
            false,
            "Test");

        var result = await new HandBrakeConnectionTester().TestAsync(installation);

        Assert.False(result.IsSuccess);
        Assert.Contains("missing", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
