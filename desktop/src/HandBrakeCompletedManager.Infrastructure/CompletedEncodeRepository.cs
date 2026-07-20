using System.Globalization;
using HandBrakeCompletedManager.Core;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class CompletedEncodeRepository(string databasePath)
{
    private static readonly string[] MigrationResourceNames =
    [
        "HandBrakeCompletedManager.Infrastructure.Migrations.001_initial.sql",
        "HandBrakeCompletedManager.Infrastructure.Migrations.002_replacement_operations.sql",
        "HandBrakeCompletedManager.Infrastructure.Migrations.003_replacement_retry_index.sql",
        "HandBrakeCompletedManager.Infrastructure.Migrations.004_original_backups.sql",
        "HandBrakeCompletedManager.Infrastructure.Migrations.005_finalization_transactions.sql"
    ];

    private readonly string _databasePath = Path.GetFullPath(databasePath);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_databasePath)
            ?? throw new InvalidOperationException("The database path has no parent directory.");
        Directory.CreateDirectory(directory);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        foreach (var migrationResourceName in MigrationResourceNames)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ReadMigration(migrationResourceName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<bool> AddAsync(
        CompletedEncode completedEncode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO completed_encodes (
                id, event_fingerprint, completed_at_utc,
                source_path, source_filename, source_extension, source_size, source_exists,
                destination_path, destination_filename, destination_extension,
                destination_size, destination_exists, destination_last_write_utc,
                output_percentage, space_saved_percentage, space_saved_bytes,
                exit_code, current_status, date_created_utc, date_updated_utc)
            VALUES (
                $id, $eventFingerprint, $completedAtUtc,
                $sourcePath, $sourceFilename, $sourceExtension, $sourceSize, $sourceExists,
                $destinationPath, $destinationFilename, $destinationExtension,
                $destinationSize, $destinationExists, $destinationLastWriteUtc,
                $outputPercentage, $spaceSavedPercentage, $spaceSavedBytes,
                $exitCode, $currentStatus, $dateCreatedUtc, $dateUpdatedUtc)
            ON CONFLICT(event_fingerprint) DO NOTHING;
            """;

        AddParameters(command, completedEncode);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> RemoveFromHistoryAsync(
        Guid recordId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM completed_encodes WHERE id = $id;";
        command.Parameters.AddWithValue("$id", recordId.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryMarkDestinationRecycledAsync(
        Guid recordId,
        string expectedDestinationPath,
        long expectedDestinationSize,
        DateTimeOffset expectedDestinationLastWriteUtc,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE completed_encodes
            SET destination_exists = 0,
                date_updated_utc = $updatedUtc
            WHERE id = $id
              AND destination_exists = 1
              AND destination_path = $destinationPath COLLATE NOCASE
              AND destination_size = $destinationSize
              AND destination_last_write_utc = $destinationLastWriteUtc;
            """;
        command.Parameters.AddWithValue("$id", recordId.ToString("D"));
        command.Parameters.AddWithValue("$destinationPath", Path.GetFullPath(expectedDestinationPath));
        command.Parameters.AddWithValue("$destinationSize", expectedDestinationSize);
        command.Parameters.AddWithValue("$destinationLastWriteUtc", FormatDate(expectedDestinationLastWriteUtc));
        command.Parameters.AddWithValue("$updatedUtc", FormatDate(updatedUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<CompletedEncode>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var records = new List<CompletedEncode>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, event_fingerprint, completed_at_utc,
                   source_path, source_filename, source_extension, source_size, source_exists,
                   destination_path, destination_filename, destination_extension,
                   destination_size, destination_exists, destination_last_write_utc,
                   output_percentage, space_saved_percentage, space_saved_bytes,
                   exit_code, current_status, date_created_utc, date_updated_utc
            FROM completed_encodes
            ORDER BY completed_at_utc DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadCompletedEncode(reader));
        }

        return records;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static void AddParameters(SqliteCommand command, CompletedEncode item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
        command.Parameters.AddWithValue("$eventFingerprint", item.EventFingerprint);
        command.Parameters.AddWithValue("$completedAtUtc", FormatDate(item.CompletedAtUtc));
        command.Parameters.AddWithValue("$sourcePath", item.SourcePath);
        command.Parameters.AddWithValue("$sourceFilename", item.SourceFilename);
        command.Parameters.AddWithValue("$sourceExtension", item.SourceExtension);
        command.Parameters.AddWithValue("$sourceSize", DbValue(item.SourceSize));
        command.Parameters.AddWithValue("$sourceExists", item.SourceExists);
        command.Parameters.AddWithValue("$destinationPath", item.DestinationPath);
        command.Parameters.AddWithValue("$destinationFilename", item.DestinationFilename);
        command.Parameters.AddWithValue("$destinationExtension", item.DestinationExtension);
        command.Parameters.AddWithValue("$destinationSize", DbValue(item.DestinationSize));
        command.Parameters.AddWithValue("$destinationExists", item.DestinationExists);
        command.Parameters.AddWithValue(
            "$destinationLastWriteUtc",
            item.DestinationLastWriteUtc is null ? DBNull.Value : FormatDate(item.DestinationLastWriteUtc.Value));
        command.Parameters.AddWithValue("$outputPercentage", DbValue(item.OutputPercentage));
        command.Parameters.AddWithValue("$spaceSavedPercentage", DbValue(item.SpaceSavedPercentage));
        command.Parameters.AddWithValue("$spaceSavedBytes", DbValue(item.SpaceSavedBytes));
        command.Parameters.AddWithValue("$exitCode", item.ExitCode);
        command.Parameters.AddWithValue("$currentStatus", item.CurrentStatus);
        command.Parameters.AddWithValue("$dateCreatedUtc", FormatDate(item.DateCreatedUtc));
        command.Parameters.AddWithValue("$dateUpdatedUtc", FormatDate(item.DateUpdatedUtc));
    }

    private static CompletedEncode ReadCompletedEncode(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        ParseDate(reader.GetString(2)),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        GetNullableInt64(reader, 6),
        reader.GetBoolean(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetString(10),
        GetNullableInt64(reader, 11),
        reader.GetBoolean(12),
        reader.IsDBNull(13) ? null : ParseDate(reader.GetString(13)),
        GetNullableDouble(reader, 14),
        GetNullableDouble(reader, 15),
        GetNullableInt64(reader, 16),
        reader.GetInt32(17),
        reader.GetString(18),
        ParseDate(reader.GetString(19)),
        ParseDate(reader.GetString(20)));

    private static string ReadMigration(string migrationResourceName)
    {
        var assembly = typeof(CompletedEncodeRepository).Assembly;
        using var stream = assembly.GetManifestResourceStream(migrationResourceName)
            ?? throw new InvalidOperationException($"Missing embedded migration: {migrationResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static object DbValue(object? value) => value ?? DBNull.Value;

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static long? GetNullableInt64(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static double? GetNullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
