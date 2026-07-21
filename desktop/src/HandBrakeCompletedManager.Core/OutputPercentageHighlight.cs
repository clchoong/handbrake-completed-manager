namespace HandBrakeCompletedManager.Core;

public enum OutputPercentageHighlight
{
    None,
    PaleOrange,
    PaleRed
}

public static class OutputPercentageHighlightRules
{
    public static OutputPercentageHighlight Classify(double? percentage) => percentage switch
    {
        >= 90 => OutputPercentageHighlight.PaleRed,
        > 80 => OutputPercentageHighlight.PaleOrange,
        _ => OutputPercentageHighlight.None
    };
}
