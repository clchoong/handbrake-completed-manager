using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class BulkReplacementPlannerTests
{
    [Fact]
    public void Review_AllowsDistinctFinalPaths()
    {
        var plans = new[]
        {
            CreatePlan(@"D:\Videos\First.mkv"),
            CreatePlan(@"D:\Videos\Second.avi")
        };

        var review = BulkReplacementPlanner.Review(plans);

        Assert.All(review, item => Assert.True(item.CanProceed));
        Assert.DoesNotContain(review.SelectMany(item => item.Issues), issue =>
            issue.Code == "BulkFinalPathConflict");
    }

    [Fact]
    public void Review_BlocksEveryPlanWithTheSameFinalPath()
    {
        var plans = new[]
        {
            CreatePlan(@"D:\Videos\Shared.mkv"),
            CreatePlan(@"D:\Videos\Shared.avi")
        };

        var review = BulkReplacementPlanner.Review(plans);

        Assert.Equal(2, review.Count);
        Assert.All(review, item =>
        {
            Assert.False(item.CanProceed);
            Assert.Contains(item.Issues, issue =>
                issue.Code == "BulkFinalPathConflict" &&
                issue.Severity == ReplacementIssueSeverity.Blocking);
        });
    }

    [Fact]
    public void Review_PreservesExistingBlockingIssues()
    {
        var plan = CreatePlan(@"D:\Videos\Blocked.mkv") with
        {
            Issues =
            [
                new ReplacementIssue(
                    "ExistingBlock",
                    ReplacementIssueSeverity.Blocking,
                    "Already blocked.")
            ]
        };

        var item = Assert.Single(BulkReplacementPlanner.Review([plan]));

        Assert.False(item.CanProceed);
        Assert.Contains(item.Issues, issue => issue.Code == "ExistingBlock");
    }

    private static ReplacementPlan CreatePlan(string sourcePath)
    {
        var timestamp = new DateTimeOffset(2026, 7, 21, 1, 2, 3, TimeSpan.Zero);
        var record = new CompletedEncode(
            Guid.NewGuid(),
            Guid.NewGuid().ToString("N"),
            timestamp,
            sourcePath,
            Path.GetFileName(sourcePath),
            Path.GetExtension(sourcePath),
            1_000,
            true,
            @"E:\Converted\Output-" + Guid.NewGuid().ToString("N") + ".mp4",
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
        return ReplacementPlanner.Create(
            record,
            new ReplacementPreflightSnapshot(
                true,
                1_000,
                true,
                400,
                false,
                false,
                timestamp));
    }
}
