namespace HandBrakeCompletedManager.Core;

public static class BulkProgressRules
{
    public static double CalculatePercentage(double processedItems, int total)
    {
        if (total <= 0) return 100;
        return Math.Clamp(processedItems * 100d / total, 0, 100);
    }
}
