namespace HandBrakeCompletedManager.Infrastructure;

public static class StoragePaths
{
    public const string DataDirectoryEnvironmentVariable = "HBCM_DATA_DIRECTORY";
    public const string PortableModeMarkerFileName = "portable.mode";

    public static string ResolveDatabasePath()
    {
        return Path.Combine(ResolveDataDirectory(), "history.db");
    }

    public static string ResolveConnectionsPath() =>
        Path.Combine(ResolveDataDirectory(), "handbrake-connections.json");

    public static string ResolveSettingsPath() =>
        Path.Combine(ResolveDataDirectory(), "settings.json");

    public static string ResolveLogsDirectory() =>
        Path.Combine(ResolveDataDirectory(), "logs");

    public static string ResolveDataDirectory()
    {
        return ResolveDataDirectory(
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    public static string ResolveDataDirectory(
        string applicationDirectory,
        string? overrideDirectory,
        string localApplicationDataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return Path.GetFullPath(overrideDirectory);
        }

        var fullApplicationDirectory = Path.GetFullPath(applicationDirectory);
        return File.Exists(Path.Combine(fullApplicationDirectory, PortableModeMarkerFileName))
            ? Path.Combine(fullApplicationDirectory, "data")
            : Path.Combine(Path.GetFullPath(localApplicationDataDirectory), "HandBrake Completed Manager");
    }
}
