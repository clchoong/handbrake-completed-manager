namespace HandBrakeCompletedManager.Infrastructure;

public static class StoragePaths
{
    public const string DataDirectoryEnvironmentVariable = "HBCM_DATA_DIRECTORY";

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
        var overrideDirectory = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.GetFullPath(overrideDirectory)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HandBrake Completed Manager");
    }
}
