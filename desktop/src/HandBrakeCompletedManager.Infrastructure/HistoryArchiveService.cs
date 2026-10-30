using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class HistoryArchiveService(string databasePath)
{
    private readonly string _databasePath = Path.GetFullPath(databasePath);

    public void Backup(string destination)
    {
        WriteNew(destination, temporary =>
        {
            using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
            }.ToString());
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporary, Mode = SqliteOpenMode.ReadWrite, Pooling = false
            }.ToString());
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        });
    }

    public async Task ExportJsonAsync(string destination)
    {
        var records = await new CompletedEncodeRepository(_databasePath).GetAllAsync().ConfigureAwait(false);
        WriteNew(destination, temporary =>
        {
            using var stream = new FileStream(temporary, FileMode.Truncate, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, records, new JsonSerializerOptions { WriteIndented = true });
        });
    }

    private void WriteNew(string destination, Action<string> write)
    {
        var fullPath = Path.GetFullPath(destination);
        if (string.Equals(fullPath, _databasePath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fullPath, _databasePath + "-wal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fullPath, _databasePath + "-shm", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a destination outside the active database files.");
        var directory = Path.GetDirectoryName(fullPath) ?? throw new IOException("Choose a valid destination.");
        var temporary = Path.Combine(directory, $".hbcm-export-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            created = true;
            write(temporary);
            File.Move(temporary, fullPath, overwrite: false);
        }
        finally
        {
            if (created && File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
