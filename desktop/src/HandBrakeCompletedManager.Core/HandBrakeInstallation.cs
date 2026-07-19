namespace HandBrakeCompletedManager.Core;

public enum HandBrakeInstallationType
{
    Installed,
    Portable,
    Unknown
}

public sealed record HandBrakeInstallation(
    string ExecutablePath,
    string? Version,
    HandBrakeInstallationType Type,
    bool Exists,
    bool IsRunning,
    string DetectionSource)
{
    public string DirectoryPath => Path.GetDirectoryName(ExecutablePath) ?? string.Empty;

    public string DisplayLabel
    {
        get
        {
            var version = string.IsNullOrWhiteSpace(Version) ? "Unknown version" : Version;
            var running = IsRunning ? " - Running" : string.Empty;
            var missing = Exists ? string.Empty : " - Missing";
            return $"HandBrake {version} ({Type}){running}{missing} - {DirectoryPath}";
        }
    }
}

public sealed record HandBrakeConnectionState(
    string ExecutablePath,
    bool IsConnected,
    DateTimeOffset? LastTestedUtc);

public sealed record ConnectionTestResult(bool IsSuccess, string Message);
