using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class StoragePathsTests
{
    [Fact]
    public void ResolveDataDirectory_UsesExplicitOverrideFirst()
    {
        var overrideDirectory = Path.Combine(Path.GetTempPath(), "hbcm-explicit-data");

        var result = StoragePaths.ResolveDataDirectory(
            Path.Combine(Path.GetTempPath(), "hbcm-app"),
            overrideDirectory,
            Path.Combine(Path.GetTempPath(), "hbcm-local-app-data"));

        Assert.Equal(Path.GetFullPath(overrideDirectory), result);
    }

    [Fact]
    public void ResolveDataDirectory_UsesPortableDataFolderWhenMarkerExists()
    {
        var applicationDirectory = Path.Combine(
            Path.GetTempPath(),
            "hbcm-storage-path-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(applicationDirectory);
        File.WriteAllText(
            Path.Combine(applicationDirectory, StoragePaths.PortableModeMarkerFileName),
            "portable");

        try
        {
            var result = StoragePaths.ResolveDataDirectory(
                applicationDirectory,
                overrideDirectory: null,
                Path.Combine(Path.GetTempPath(), "hbcm-local-app-data"));

            Assert.Equal(Path.Combine(applicationDirectory, "data"), result);
        }
        finally
        {
            Directory.Delete(applicationDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResolveDataDirectory_UsesLocalAppDataWithoutPortableMarker()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), "hbcm-app");
        var localApplicationData = Path.Combine(Path.GetTempPath(), "hbcm-local-app-data");

        var result = StoragePaths.ResolveDataDirectory(
            applicationDirectory,
            overrideDirectory: null,
            localApplicationData);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(localApplicationData), "HandBrake Completed Manager"),
            result);
    }
}
