using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class CompletionEventParserTests
{
    [Fact]
    public void Parse_ReadsHandBrakeEnvironmentVariables()
    {
        var environment = new Dictionary<string, string?>
        {
            ["HB_SOURCE"] = @"D:\Videos\Holiday.mov",
            ["HB_DESTINATION"] = @"E:\Converted\Holiday.mp4",
            ["HB_DESTINATION_FOLDER"] = @"E:\Converted",
            ["HB_EXIT_CODE"] = "0"
        };
        var completedAt = new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);

        var result = CompletionEventParser.Parse(
            [],
            name => environment.GetValueOrDefault(name),
            completedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(@"D:\Videos\Holiday.mov", result.Event!.SourcePath);
        Assert.Equal(@"E:\Converted\Holiday.mp4", result.Event.DestinationPath);
        Assert.Equal(@"E:\Converted", result.Event.DestinationFolder);
        Assert.Equal(0, result.Event.ExitCode);
        Assert.Equal(completedAt, result.Event.CompletedAtUtc);
    }

    [Fact]
    public void Parse_CommandLineOverridesEnvironment()
    {
        string[] args =
        [
            "--source", @"D:\New Source.mkv",
            @"--destination=D:\New Output.mp4",
            "--exit-code", "7"
        ];

        var result = CompletionEventParser.Parse(args, _ => @"C:\Old.mkv");

        Assert.True(result.IsSuccess);
        Assert.Equal(@"D:\New Source.mkv", result.Event!.SourcePath);
        Assert.Equal(@"D:\New Output.mp4", result.Event.DestinationPath);
        Assert.Equal(7, result.Event.ExitCode);
    }

    [Fact]
    public void Parse_RejectsMissingSource()
    {
        var result = CompletionEventParser.Parse(
            ["--destination", @"D:\Output.mp4"],
            _ => null);

        Assert.False(result.IsSuccess);
        Assert.Contains("source path", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsInvalidExitCode()
    {
        var result = CompletionEventParser.Parse(
            ["--source", @"D:\Source.mkv", "--destination", @"D:\Output.mp4", "--exit-code", "bad"],
            _ => null);

        Assert.False(result.IsSuccess);
        Assert.Contains("valid integer", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
