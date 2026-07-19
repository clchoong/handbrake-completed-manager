using HandBrakeCompletedManager.Core;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class HandBrakeConnectionTester
{
    public async Task<ConnectionTestResult> TestAsync(
        HandBrakeInstallation installation,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(installation.ExecutablePath))
        {
            return new ConnectionTestResult(false, "The selected HandBrake executable is missing.");
        }

        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "HandBrake Completed Manager",
            "connection-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var repository = new CompletedEncodeRepository(Path.Combine(testDirectory, "history.db"));
            await repository.InitializeAsync(cancellationToken);

            var completionEvent = new CompletionEvent(
                installation.ExecutablePath,
                installation.ExecutablePath,
                installation.DirectoryPath,
                0,
                DateTimeOffset.UtcNow);
            var record = CompletedEncodeCapture.Create(completionEvent);
            var inserted = await repository.AddAsync(record, cancellationToken);
            var records = await repository.GetAllAsync(cancellationToken);

            return inserted && records.Count == 1
                ? new ConnectionTestResult(true, "Connection test passed. The simulated event reached SQLite.")
                : new ConnectionTestResult(false, "The simulated completion event was not persisted correctly.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
        {
            return new ConnectionTestResult(false, $"Connection test failed: {exception.Message}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDeleteTestDirectory(testDirectory);
        }
    }

    private static void TryDeleteTestDirectory(string testDirectory)
    {
        try
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temporary test data can be cleaned by Windows if a native SQLite handle closes late.
        }
        catch (UnauthorizedAccessException)
        {
            // Failure to clean temporary test data does not change the test result.
        }
    }
}

