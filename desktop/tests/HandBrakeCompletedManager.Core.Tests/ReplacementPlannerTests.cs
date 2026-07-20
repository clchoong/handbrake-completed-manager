using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class ReplacementPlannerTests
{
    [Fact]
    public void Create_BuildsSafePathsAndAllowsUnchangedAvailableFiles()
    {
        var record = CreateRecord();

        var plan = ReplacementPlanner.Create(
            record,
            new ReplacementPreflightSnapshot(true, 1_000, true, 400, false, false));

        Assert.True(plan.CanProceed);
        Assert.Empty(plan.Issues);
        Assert.Equal(@"D:\Videos\Source.mp4", plan.Paths.FinalPath);
        Assert.Equal(@"D:\Videos\Source.mp4.hbcm-copying", plan.Paths.TemporaryPath);
        Assert.Equal(@"D:\Videos\HandBrake Original Backup\Source.mkv", plan.Paths.BackupPath);
    }

    [Theory]
    [InlineData(false, 1000, true, 400, false, false, "SourceMissing")]
    [InlineData(true, 1000, false, 400, false, false, "DestinationMissing")]
    [InlineData(true, 0, true, 400, false, false, "SourceEmpty")]
    [InlineData(true, 1000, true, 0, false, false, "DestinationEmpty")]
    [InlineData(true, 1000, true, 400, true, false, "FinalPathConflict")]
    [InlineData(true, 1000, true, 400, false, true, "TemporaryPathConflict")]
    [InlineData(true, 999, true, 400, false, false, "SourceChanged")]
    [InlineData(true, 1000, true, 399, false, false, "DestinationChanged")]
    public void Create_BlocksUnsafeSnapshots(
        bool sourceExists,
        int sourceSize,
        bool destinationExists,
        int destinationSize,
        bool finalPathExists,
        bool temporaryPathExists,
        string expectedCode)
    {
        var plan = ReplacementPlanner.Create(
            CreateRecord(),
            new ReplacementPreflightSnapshot(
                sourceExists,
                (long?)sourceSize,
                destinationExists,
                (long?)destinationSize,
                finalPathExists,
                temporaryPathExists));

        Assert.False(plan.CanProceed);
        Assert.Contains(plan.Issues, issue =>
            issue.Code == expectedCode && issue.Severity == ReplacementIssueSeverity.Blocking);
    }

    [Fact]
    public void Create_WarnsWhenOutputIsLargerButDoesNotBlock()
    {
        var record = CreateRecord() with { SourceSize = 1_000, DestinationSize = 1_200 };

        var plan = ReplacementPlanner.Create(
            record,
            new ReplacementPreflightSnapshot(true, 1_000, true, 1_200, false, false));

        Assert.True(plan.CanProceed);
        var issue = Assert.Single(plan.Issues);
        Assert.Equal("OutputLargerThanSource", issue.Code);
        Assert.Equal(ReplacementIssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void Create_BlocksWhenSourceAndDestinationAreTheSameFile()
    {
        var record = CreateRecord() with
        {
            DestinationPath = @"D:\Videos\Source.mkv",
            DestinationFilename = "Source.mkv",
            DestinationExtension = ".mkv"
        };

        var plan = ReplacementPlanner.Create(
            record,
            new ReplacementPreflightSnapshot(true, 1_000, true, 1_000, true, false));

        Assert.False(plan.CanProceed);
        Assert.Contains(plan.Issues, issue => issue.Code == "SameFile");
    }

    [Fact]
    public void Create_AllowsDifferentFilesWithSameExtensionAndTargetsOriginalPath()
    {
        var record = CreateRecord() with
        {
            DestinationPath = @"E:\Converted\Output.mkv",
            DestinationFilename = "Output.mkv",
            DestinationExtension = ".mkv"
        };

        var plan = ReplacementPlanner.Create(
            record,
            new ReplacementPreflightSnapshot(true, 1_000, true, 400, true, false));

        Assert.True(plan.CanProceed);
        Assert.Empty(plan.Issues);
        Assert.Equal(record.SourcePath, plan.Paths.FinalPath, ignoreCase: true);
    }

    [Fact]
    public void Create_BlocksChangedDestinationTimestamp()
    {
        var record = CreateRecord();
        var changedTimestamp = record.DestinationLastWriteUtc!.Value.AddSeconds(1);

        var plan = ReplacementPlanner.Create(
            record,
            new ReplacementPreflightSnapshot(
                true,
                1_000,
                true,
                400,
                false,
                false,
                changedTimestamp));

        Assert.False(plan.CanProceed);
        Assert.Contains(plan.Issues, issue => issue.Code == "DestinationTimestampChanged");
    }

    [Fact]
    public void Create_BlocksExistingOriginalBackupPath()
    {
        var record = CreateRecord();

        var plan = ReplacementPlanner.Create(
            record,
            new ReplacementPreflightSnapshot(
                true,
                1_000,
                true,
                400,
                false,
                false,
                record.DestinationLastWriteUtc,
                BackupPathExists: true));

        Assert.False(plan.CanProceed);
        Assert.Contains(plan.Issues, issue => issue.Code == "BackupPathConflict");
    }

    private static CompletedEncode CreateRecord()
    {
        var timestamp = new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);
        return new CompletedEncode(
            Guid.NewGuid(),
            "REPLACEMENT-TEST",
            timestamp,
            @"D:\Videos\Source.mkv",
            "Source.mkv",
            ".mkv",
            1_000,
            true,
            @"E:\Converted\Output.mp4",
            "Output.mp4",
            ".mp4",
            400,
            true,
            timestamp,
            40,
            60,
            600,
            0,
            "Completed",
            timestamp,
            timestamp);
    }
}
