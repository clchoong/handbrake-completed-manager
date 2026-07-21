namespace HandBrakeCompletedManager.Core;

public enum OutputPercentageHighlight
{
    None,
    Orange,
    Red
}

public static class OutputPercentageHighlightRules
{
    public static OutputPercentageHighlight Classify(double? percentage) => percentage switch
    {
        >= 90 => OutputPercentageHighlight.Red,
        >= 80 => OutputPercentageHighlight.Orange,
        _ => OutputPercentageHighlight.None
    };
}
