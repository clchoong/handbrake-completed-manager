using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class CompletedEncodeFilterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Matches_SearchesAcrossSourceDestinationAndStatus()
    {
        var item = CreateItem(
            sourcePath: @"D:\Camera\Family Holiday.mov",
            destinationPath: @"E:\Converted\Family Holiday.mp4",
            status: "Awaiting Review");

        Assert.True(CompletedEncodeFilter.Matches(
            item, "holiday awaiting", CompletedEncodeQuickFilter.All, Now));
        Assert.False(CompletedEncodeFilter.Matches(
            item, "holiday missing", CompletedEncodeQuickFilter.All, Now));
    }

    [Fact]
    public void Matches_TodayUsesProvidedTimeZone()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Test +08", TimeSpan.FromHours(8), "Test +08", "Test +08");
        var item = CreateItem(completedAtUtc: new DateTimeOffset(2026, 7, 19, 18, 0, 0, TimeSpan.Zero));

        Assert.True(CompletedEncodeFilter.Matches(
            item, null, CompletedEncodeQuickFilter.Today, Now, timeZone));
    }

    [Theory]
    [InlineData(-6, true)]
    [InlineData(-8, false)]
    [InlineData(1, false)]
    public void Matches_LastSevenDaysUsesBoundedUtcWindow(int dayOffset, bool expected)
    {
        var item = CreateItem(completedAtUtc: Now.AddDays(dayOffset));

        var actual = CompletedEncodeFilter.Matches(
            item, null, CompletedEncodeQuickFilter.LastSevenDays, Now, TimeZoneInfo.Utc);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Matches_MissingFilesIncludesEitherMissingSide()
    {
        var item = CreateItem(sourceExists: true, destinationExists: false);

        Assert.True(CompletedEncodeFilter.Matches(
            item, null, CompletedEncodeQuickFilter.MissingFiles, Now));
    }

    [Theory]
    [InlineData(1000, 1001, true)]
    [InlineData(1000, 1000, false)]
    [InlineData(1000, 500, false)]
    public void Matches_OutputLargerThanSourceComparesRawSizes(
        long sourceSize,
        long destinationSize,
        bool expected)
    {
        var item = CreateItem(sourceSize: sourceSize, destinationSize: destinationSize);

        var actual = CompletedEncodeFilter.Matches(
            item, null, CompletedEncodeQuickFilter.OutputLargerThanSource, Now);

        Assert.Equal(expected, actual);
    }

    private static CompletedEncode CreateItem(
        string sourcePath = @"D:\Videos\Source.mkv",
        string destinationPath = @"E:\Output\Source.mp4",
        string status = "Completed",
        DateTimeOffset? completedAtUtc = null,
        bool sourceExists = true,
        bool destinationExists = true,
        long? sourceSize = 1000,
        long? destinationSize = 500) => new(
        Guid.NewGuid(),
        "fingerprint",
        completedAtUtc ?? Now,
        sourcePath,
        Path.GetFileName(sourcePath),
        Path.GetExtension(sourcePath),
        sourceSize,
        sourceExists,
        destinationPath,
        Path.GetFileName(destinationPath),
        Path.GetExtension(destinationPath),
        destinationSize,
        destinationExists,
        completedAtUtc ?? Now,
        50,
        50,
        500,
        0,
        status,
        completedAtUtc ?? Now,
        completedAtUtc ?? Now);
}
