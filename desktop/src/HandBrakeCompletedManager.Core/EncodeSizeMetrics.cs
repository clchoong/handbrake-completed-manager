namespace HandBrakeCompletedManager.Core;

public sealed record EncodeSizeMetrics(
    double? OutputPercentage,
    double? SpaceSavedPercentage,
    long? SpaceSavedBytes)
{
    public static EncodeSizeMetrics Calculate(long? sourceSize, long? destinationSize)
    {
        if (sourceSize is null or <= 0 || destinationSize is null or < 0)
        {
            return new EncodeSizeMetrics(null, null, null);
        }

        var outputPercentage = destinationSize.Value * 100d / sourceSize.Value;
        return new EncodeSizeMetrics(
            outputPercentage,
            100d - outputPercentage,
            sourceSize.Value - destinationSize.Value);
    }
}

