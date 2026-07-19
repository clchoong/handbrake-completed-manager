using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class HandBrakeCompletionSetupTests
{
    [Fact]
    public void Create_ReturnsFullReceiverPathAndRecommendedArguments()
    {
        var relativePath = Path.Combine("tools", "HandBrakeCompletedManager.Receiver.exe");

        var setup = HandBrakeCompletionSetup.Create(relativePath);

        Assert.Equal(Path.GetFullPath(relativePath), setup.ReceiverPath);
        Assert.Equal(
            "--source {source} --destination {destination} " +
            "--destination-folder {destination_folder} --exit-code {exit_code}",
            setup.Arguments);
    }

    [Fact]
    public void Create_ReportsExistingReceiver()
    {
        var path = Path.GetTempFileName();

        try
        {
            Assert.True(HandBrakeCompletionSetup.Create(path).ReceiverExists);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Create_RejectsBlankReceiverPath()
    {
        Assert.Throws<ArgumentException>(() => HandBrakeCompletionSetup.Create("  "));
    }
}
