using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class BulkProgressRulesTests
{
    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(1, 5, 20)]
    [InlineData(3, 5, 60)]
    [InlineData(5, 5, 100)]
    [InlineData(0, 0, 100)]
    public void CalculatePercentage_UsesProcessedItemCount(
        int processed,
        int total,
        double expected)
    {
        Assert.Equal(expected, BulkProgressRules.CalculatePercentage(processed, total));
    }
}
