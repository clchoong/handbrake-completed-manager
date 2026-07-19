using System.ComponentModel;
using System.Diagnostics;
using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class WindowsFileActionServiceTests
{
    [Fact]
    public void Play_UsesWindowsDefaultApplicationForExistingFile()
    {
        var path = Path.GetTempFileName();
        ProcessStartInfo? captured = null;
        var service = new WindowsFileActionService(startInfo => captured = startInfo);

        try
        {
            var result = service.Play(path);

            Assert.True(result.IsSuccess);
            Assert.NotNull(captured);
            Assert.Equal(Path.GetFullPath(path), captured.FileName);
            Assert.True(captured.UseShellExecute);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reveal_SelectsExistingFileInExplorer()
    {
        var path = Path.GetTempFileName();
        ProcessStartInfo? captured = null;
        var service = new WindowsFileActionService(startInfo => captured = startInfo);

        try
        {
            var result = service.Reveal(path);

            Assert.True(result.IsSuccess);
            Assert.NotNull(captured);
            Assert.Equal("explorer.exe", captured.FileName);
            Assert.Equal($"/select,\"{Path.GetFullPath(path)}\"", captured.Arguments);
            Assert.True(captured.UseShellExecute);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Play_DoesNotLaunchMissingFile()
    {
        var launched = false;
        var service = new WindowsFileActionService(_ => launched = true);

        var result = service.Play(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.False(result.IsSuccess);
        Assert.False(launched);
        Assert.Contains("no longer exists", result.Message);
    }

    [Fact]
    public void Reveal_RejectsBlankPath()
    {
        var service = new WindowsFileActionService(_ => throw new InvalidOperationException());

        var result = service.Reveal("  ");

        Assert.False(result.IsSuccess);
        Assert.Contains("No file path", result.Message);
    }

    [Fact]
    public void Play_ReportsWindowsLaunchFailure()
    {
        var path = Path.GetTempFileName();
        var service = new WindowsFileActionService(_ => throw new Win32Exception("No association"));

        try
        {
            var result = service.Play(path);

            Assert.False(result.IsSuccess);
            Assert.Contains("No association", result.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OpenFolder_UsesWindowsShellForExistingDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hbcm-folder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        ProcessStartInfo? captured = null;
        var service = new WindowsFileActionService(startInfo => captured = startInfo);

        try
        {
            var result = service.OpenFolder(directory);

            Assert.True(result.IsSuccess);
            Assert.NotNull(captured);
            Assert.Equal(Path.GetFullPath(directory), captured.FileName);
            Assert.True(captured.UseShellExecute);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }
}
