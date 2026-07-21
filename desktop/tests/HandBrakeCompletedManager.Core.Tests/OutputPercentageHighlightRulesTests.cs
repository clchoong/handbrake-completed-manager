using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class OutputPercentageHighlightRulesTests
{
    [Theory]
    [InlineData(null, OutputPercentageHighlight.None)]
    [InlineData(80.0, OutputPercentageHighlight.None)]
    [InlineData(80.01, OutputPercentageHighlight.PaleOrange)]
    [InlineData(89.99, OutputPercentageHighlight.PaleOrange)]
    [InlineData(90.0, OutputPercentageHighlight.PaleRed)]
    [InlineData(100.0, OutputPercentageHighlight.PaleRed)]
    public void Classify_UsesRequestedThresholds(
        double? percentage,
        OutputPercentageHighlight expected)
    {
        Assert.Equal(expected, OutputPercentageHighlightRules.Classify(percentage));
    }
}
