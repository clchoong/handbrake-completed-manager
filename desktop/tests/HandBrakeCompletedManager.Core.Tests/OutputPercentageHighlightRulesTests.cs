using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class OutputPercentageHighlightRulesTests
{
    [Theory]
    [InlineData(null, OutputPercentageHighlight.None)]
    [InlineData(79.99, OutputPercentageHighlight.None)]
    [InlineData(80.0, OutputPercentageHighlight.Orange)]
    [InlineData(89.99, OutputPercentageHighlight.Orange)]
    [InlineData(90.0, OutputPercentageHighlight.Red)]
    [InlineData(100.0, OutputPercentageHighlight.Red)]
    public void Classify_UsesRequestedThresholds(
        double? percentage,
        OutputPercentageHighlight expected)
    {
        Assert.Equal(expected, OutputPercentageHighlightRules.Classify(percentage));
    }
}
