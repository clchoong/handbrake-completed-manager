using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class CompletedEncodeCaptureTests
{
    [Fact]
    public void Create_CapturesFileMetadataAndSizeReduction()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "handbrake-completed-manager-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var sourcePath = Path.Combine(testDirectory, "Source.mkv");
        var destinationPath = Path.Combine(testDirectory, "Output.mp4");

        try
        {
            File.WriteAllBytes(sourcePath, new byte[1_000]);
            File.WriteAllBytes(destinationPath, new byte[400]);
            var completionEvent = new CompletionEvent(
                sourcePath,
                destinationPath,
                testDirectory,
                0,
                DateTimeOffset.UtcNow);

            var record = CompletedEncodeCapture.Create(completionEvent);

            Assert.True(record.SourceExists);
            Assert.True(record.DestinationExists);
            Assert.Equal(1_000, record.SourceSize);
            Assert.Equal(400, record.DestinationSize);
            Assert.Equal(40d, record.OutputPercentage);
            Assert.Equal(60d, record.SpaceSavedPercentage);
            Assert.Equal(600, record.SpaceSavedBytes);
            Assert.Equal("Completed", record.CurrentStatus);
            Assert.NotEmpty(record.EventFingerprint);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}
