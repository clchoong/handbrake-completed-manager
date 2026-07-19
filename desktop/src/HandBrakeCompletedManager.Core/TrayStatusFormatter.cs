using System.Globalization;

namespace HandBrakeCompletedManager.Core;

public static class TrayStatusFormatter
{
    public static string FormatRecordCount(int recordCount, CultureInfo? culture = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);

        var number = recordCount.ToString("N0", culture ?? CultureInfo.CurrentCulture);
        var noun = recordCount == 1 ? "record" : "records";
        return $"HandBrake Completed Manager - {number} {noun}";
    }
}
