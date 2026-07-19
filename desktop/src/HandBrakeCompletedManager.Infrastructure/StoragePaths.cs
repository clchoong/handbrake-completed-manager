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

    private static string ResolveDataDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.GetFullPath(overrideDirectory)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HandBrake Completed Manager");
    }
}
