using System.ComponentModel;
using System.Diagnostics;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed record FileActionResult(bool IsSuccess, string Message);

public sealed class WindowsFileActionService
{
    private readonly Action<ProcessStartInfo> _startProcess;

    public WindowsFileActionService(Action<ProcessStartInfo>? startProcess = null)
    {
        _startProcess = startProcess ?? (startInfo => _ = Process.Start(startInfo));
    }

    public FileActionResult Play(string filePath) => ExecuteForExistingFile(
        filePath,
        fullPath => new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true
        },
        fullPath => $"Opened {Path.GetFileName(fullPath)} in the default application.");

    public FileActionResult Reveal(string filePath) => ExecuteForExistingFile(
        filePath,
        fullPath => new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{fullPath}\"",
            UseShellExecute = true
        },
        fullPath => $"Selected {Path.GetFileName(fullPath)} in File Explorer.");

    private FileActionResult ExecuteForExistingFile(
        string filePath,
        Func<string, ProcessStartInfo> createStartInfo,
        Func<string, string> createSuccessMessage)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new FileActionResult(false, "No file path is available for this record.");
        }

        try
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                return new FileActionResult(false, $"The file no longer exists: {fullPath}");
            }

            _startProcess(createStartInfo(fullPath));
            return new FileActionResult(true, createSuccessMessage(fullPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or
                IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            return new FileActionResult(false, $"Unable to open the file: {exception.Message}");
        }
    }
}
