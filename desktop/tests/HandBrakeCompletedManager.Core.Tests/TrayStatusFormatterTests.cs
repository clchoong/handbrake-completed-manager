using System.Globalization;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class TrayStatusFormatterTests
{
    [Theory]
    [InlineData(0, "0 records")]
    [InlineData(1, "1 record")]
    [InlineData(1234, "1,234 records")]
    [InlineData(int.MaxValue, "2,147,483,647 records")]
    public void FormatRecordCount_ProducesWindowsTraySafeStatus(int count, string expectedEnding)
    {
        var result = TrayStatusFormatter.FormatRecordCount(count, CultureInfo.InvariantCulture);

        Assert.EndsWith(expectedEnding, result);
        Assert.True(result.Length <= 63);
    }

    [Fact]
    public void FormatRecordCount_RejectsNegativeCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrayStatusFormatter.FormatRecordCount(-1));
    }
}
