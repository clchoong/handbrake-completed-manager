using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class HandBrakeActivityLogParserTests
{
    [Fact]
    public void Parse_RecoversSuccessfulEncodePathsAndCompletionTime()
    {
        const string content = """
            HandBrake 1.11.2
            [21:53:47] json job:
              "Destination": {
                "File": "C:\\Converted\\Output.mp4",
                "Mux": "av_mp4"
              },
              "Source": {
                "Path": "D:\\Originals\\Source.mkv",
                "Title": 1
              }
            [21:53:45] Finished work at: Mon Jul 20 21:53:45 2026
            [21:53:45] libhb: work result = 0
             # Job Completed!
            """;

        var result = HandBrakeActivityLogParser.Parse(
            content,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.True(result.IsRecoverable);
        Assert.Equal(HandBrakeActivityLogStatus.Completed, result.Status);
        Assert.NotNull(result.CompletionEvent);
        Assert.Equal(@"D:\Originals\Source.mkv", result.CompletionEvent.SourcePath);
        Assert.Equal(@"C:\Converted\Output.mp4", result.CompletionEvent.DestinationPath);
        Assert.Equal(0, result.CompletionEvent.ExitCode);
        Assert.Equal(2026, result.CompletionEvent.CompletedAtUtc.Year);
        Assert.Equal(7, result.CompletionEvent.CompletedAtUtc.Month);
        Assert.Equal(20, result.CompletionEvent.CompletedAtUtc.ToLocalTime().Day);
    }

    [Fact]
    public void Parse_RejectsPausedOrIncompleteLog()
    {
        var result = HandBrakeActivityLogParser.Parse(
            "# Starting Encode ...\n# Encode Paused",
            DateTimeOffset.UtcNow);

        Assert.Equal(HandBrakeActivityLogStatus.Incomplete, result.Status);
        Assert.False(result.IsRecoverable);
        Assert.Null(result.CompletionEvent);
    }

    [Fact]
    public void Parse_RejectsFailedEncode()
    {
        var result = HandBrakeActivityLogParser.Parse(
            "libhb: work result = 3\n# Job Completed!",
            DateTimeOffset.UtcNow);

        Assert.Equal(HandBrakeActivityLogStatus.Failed, result.Status);
        Assert.Contains("result 3", result.Message);
    }

    [Fact]
    public void Parse_UsesFallbackTimestampWhenFinishedLineIsUnavailable()
    {
        var fallback = new DateTimeOffset(2026, 7, 19, 4, 5, 6, TimeSpan.Zero);
        const string content = """
              "Destination": { "File": "C:\\Converted\\Output.mp4" },
              "Source": { "Path": "D:\\Originals\\Source.mkv" }
            libhb: work result = 0
            # Job Completed!
            """;

        var result = HandBrakeActivityLogParser.Parse(content, fallback);

        Assert.True(result.IsRecoverable);
        Assert.Equal(fallback, result.CompletionEvent!.CompletedAtUtc);
    }
}
