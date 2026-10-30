using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class HistoryArchiveServiceTests
{
    [Fact]
    public async Task ExportWritesJsonAndLeavesExistingFileUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hbcm-archive-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "history.db");
        var output = Path.Combine(directory, "history.json");
        try
        {
            await new CompletedEncodeRepository(source).InitializeAsync();
            var service = new HistoryArchiveService(source);
            await service.ExportJsonAsync(output);
            Assert.Equal("[]", (await File.ReadAllTextAsync(output)).Trim());
            await Assert.ThrowsAsync<IOException>(() => service.ExportJsonAsync(output));
            Assert.Equal("[]", (await File.ReadAllTextAsync(output)).Trim());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BackupIncludesUncheckpointedWalAndRefusesOverwrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hbcm-archive-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "history.db");
        var backup = Path.Combine(directory, "backup.db");
        try
        {
            using var connection = new SqliteConnection($"Data Source={source};Pooling=False");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE example (value TEXT); INSERT INTO example VALUES ('saved');";
            await command.ExecuteNonQueryAsync();
            var service = new HistoryArchiveService(source);
            service.Backup(backup);
            using var copy = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False");
            await copy.OpenAsync();
            using var verify = copy.CreateCommand();
            verify.CommandText = "SELECT value FROM example";
            Assert.Equal("saved", await verify.ExecuteScalarAsync());
            Assert.Throws<IOException>(() => service.Backup(backup));
            Assert.Throws<IOException>(() => service.Backup(source));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
