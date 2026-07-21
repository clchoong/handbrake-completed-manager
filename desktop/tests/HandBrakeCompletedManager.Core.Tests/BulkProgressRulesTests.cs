using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class BulkProgressRulesTests
{
    [Theory]
    [InlineData(0.0, 5, 0)]
    [InlineData(0.5, 5, 10)]
    [InlineData(1.0, 5, 20)]
    [InlineData(1.5, 5, 30)]
    [InlineData(3.0, 5, 60)]
    [InlineData(5.0, 5, 100)]
    [InlineData(0.0, 0, 100)]
    public void CalculatePercentage_UsesProcessedItemCount(
        double processed,
        int total,
        double expected)
    {
        Assert.Equal(expected, BulkProgressRules.CalculatePercentage(processed, total));
    }
}
