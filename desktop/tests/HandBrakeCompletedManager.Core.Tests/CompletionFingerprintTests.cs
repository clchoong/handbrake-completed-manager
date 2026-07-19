using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class CompletionFingerprintTests
{
    [Fact]
    public void Create_IsStableForRepeatedCallback()
    {
        var lastWrite = new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);

        var first = CompletionFingerprint.Create(
            @"D:\Videos\Source.mkv", @"E:\Output\Result.mp4", 1_000, 500, lastWrite, lastWrite);
        var second = CompletionFingerprint.Create(
            @"d:\videos\source.mkv", @"e:\output\result.mp4", 1_000, 500, lastWrite, lastWrite.AddSeconds(30));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Create_ChangesWhenOutputIsRewritten()
    {
        var firstWrite = new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);

        var first = CompletionFingerprint.Create(
            @"D:\Source.mkv", @"E:\Result.mp4", 1_000, 500, firstWrite, firstWrite);
        var second = CompletionFingerprint.Create(
            @"D:\Source.mkv", @"E:\Result.mp4", 1_000, 500, firstWrite.AddMinutes(5), firstWrite.AddMinutes(5));

        Assert.NotEqual(first, second);
    }
}

