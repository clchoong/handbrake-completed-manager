using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class HandBrakeDetectorTests
{
    [Fact]
    public async Task DetectAsync_FindsPortableExecutableInSelectedFolder()
    {
        var testDirectory = CreateTestDirectory();
        var portableDirectory = Path.Combine(testDirectory, "Portable HandBrake");
        Directory.CreateDirectory(portableDirectory);
        var executablePath = Path.Combine(portableDirectory, "HandBrake.exe");
        await File.WriteAllBytesAsync(executablePath, [0x4D, 0x5A]);

        try
        {
            var detector = new HandBrakeDetector();

            var results = await detector.DetectAsync([testDirectory]);

            var installation = Assert.Single(results, item =>
                item.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(HandBrakeInstallationType.Portable, installation.Type);
            Assert.Contains("Selected folder", installation.DetectionSource);
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

