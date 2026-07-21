namespace HandBrakeCompletedManager.Core;

public static class BulkProgressRules
{
    public static double CalculatePercentage(int processed, int total)
    {
        if (total <= 0) return 100;
        return Math.Clamp(processed * 100d / total, 0, 100);
    }
}
