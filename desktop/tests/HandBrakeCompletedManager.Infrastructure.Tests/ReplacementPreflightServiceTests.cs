using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class ReplacementPreflightServiceTests
{
    [Fact]
    public async Task Review_UsesCurrentFileStateWithoutChangingFiles()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "hbcm-preflight-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "Source.mkv");
        var destinationPath = Path.Combine(directory, "Output.mp4");
        await File.WriteAllBytesAsync(sourcePath, new byte[1_000]);
        await File.WriteAllBytesAsync(destinationPath, new byte[400]);
        var record = CreateRecord(sourcePath, destinationPath, 1_000, 400);

        try
        {
            var plan = new ReplacementPreflightService().Review(record);

            Assert.True(plan.CanProceed);
            Assert.True(File.Exists(sourcePath));
            Assert.True(File.Exists(destinationPath));
            Assert.False(File.Exists(plan.Paths.FinalPath));
            Assert.False(File.Exists(plan.Paths.TemporaryPath));
            Assert.Equal(1_000, new FileInfo(sourcePath).Length);
            Assert.Equal(400, new FileInfo(destinationPath).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Review_BlocksChangedSourceAndExistingTemporaryPath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "hbcm-preflight-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "Source.mkv");
        var destinationPath = Path.Combine(directory, "Output.mp4");
        await File.WriteAllBytesAsync(sourcePath, new byte[1_001]);
        await File.WriteAllBytesAsync(destinationPath, new byte[400]);
        var record = CreateRecord(sourcePath, destinationPath, 1_000, 400);
        var paths = ReplacementPlanner.BuildPaths(record);
        await File.WriteAllTextAsync(paths.TemporaryPath, "partial");

        try
        {
            var plan = new ReplacementPreflightService().Review(record);

            Assert.False(plan.CanProceed);
            Assert.Contains(plan.Issues, issue => issue.Code == "SourceChanged");
            Assert.Contains(plan.Issues, issue => issue.Code == "TemporaryPathConflict");
            Assert.Equal("partial", await File.ReadAllTextAsync(paths.TemporaryPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CompletedEncode CreateRecord(
        string sourcePath,
        string destinationPath,
        long sourceSize,
        long destinationSize)
    {
        var timestamp = new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);
        return new CompletedEncode(
            Guid.NewGuid(),
            "PREFLIGHT-TEST",
            timestamp,
            sourcePath,
            Path.GetFileName(sourcePath),
            Path.GetExtension(sourcePath),
            sourceSize,
            true,
            destinationPath,
            Path.GetFileName(destinationPath),
            Path.GetExtension(destinationPath),
            destinationSize,
            true,
            timestamp,
            destinationSize * 100d / sourceSize,
            100d - (destinationSize * 100d / sourceSize),
            sourceSize - destinationSize,
            0,
            "Completed",
            timestamp,
            timestamp);
    }
}
