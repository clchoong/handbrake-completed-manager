using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class DiagnosticLoggerTests
{
    [Fact]
    public async Task LogAsync_WritesStructuredSingleLineEntry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hbcm-log-tests", Guid.NewGuid().ToString("N"));
        var timestamp = new DateTimeOffset(2026, 7, 20, 10, 11, 12, TimeSpan.FromHours(8));
        var logger = new DiagnosticLogger(directory, "Desktop", () => timestamp);

        try
        {
            var written = await logger.LogAsync(
                DiagnosticLogLevel.Error,
                "History\r\nload failed",
                new InvalidOperationException("Database\nlocked"));
            var logPath = Path.Combine(directory, "handbrake-completed-manager-20260720.log");
            var lines = await File.ReadAllLinesAsync(logPath);

            Assert.True(written);
            var line = Assert.Single(lines);
            Assert.Contains("[Error] [Desktop] History  load failed", line);
            Assert.Contains("InvalidOperationException: Database locked", line);
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
    public async Task LogAsync_ReturnsFalseInsteadOfThrowingWhenDirectoryCannotBeCreated()
    {
        var filePath = Path.GetTempFileName();
        var logger = new DiagnosticLogger(filePath, "Receiver");

        try
        {
            Assert.False(await logger.LogAsync(DiagnosticLogLevel.Information, "Test"));
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
