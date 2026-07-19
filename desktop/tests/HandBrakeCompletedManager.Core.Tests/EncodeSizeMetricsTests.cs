using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class EncodeSizeMetricsTests
{
    [Fact]
    public void Calculate_ReturnsExpectedReduction()
    {
        var metrics = EncodeSizeMetrics.Calculate(10_000, 4_000);

        Assert.Equal(40d, metrics.OutputPercentage);
        Assert.Equal(60d, metrics.SpaceSavedPercentage);
        Assert.Equal(6_000, metrics.SpaceSavedBytes);
    }

    [Theory]
    [InlineData(null, 100L)]
    [InlineData(0L, 100L)]
    [InlineData(100L, null)]
    [InlineData(100L, -1L)]
    public void Calculate_ReturnsUnknownWhenSizesCannotBeCompared(long? source, long? destination)
    {
        var metrics = EncodeSizeMetrics.Calculate(source, destination);

        Assert.Null(metrics.OutputPercentage);
        Assert.Null(metrics.SpaceSavedPercentage);
        Assert.Null(metrics.SpaceSavedBytes);
    }
}
