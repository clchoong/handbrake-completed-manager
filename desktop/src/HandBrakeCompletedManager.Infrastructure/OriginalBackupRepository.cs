using System.Globalization;
using HandBrakeCompletedManager.Core;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class OriginalBackupRepository(string databasePath)
{
    private readonly string _databasePath = Path.GetFullPath(databasePath);

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        new CompletedEncodeRepository(_databasePath).InitializeAsync(cancellationToken);

    public async Task<bool> TryBeginAsync(
        OriginalBackupState backup,
        DateTimeOffset expectedOperationUpdatedUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using (var operationCommand = connection.CreateCommand())
        {
            operationCommand.Transaction = transaction;
            operationCommand.CommandText = """
                UPDATE replacement_operations
                SET stage = 'BackingUpSource',
                    failure_message = NULL,
                    date_updated_utc = $updatedUtc
                WHERE id = $operationId
                  AND date_updated_utc = $expectedUpdatedUtc
                  AND status = 'InProgress'
                  AND stage = 'Verifying'
                  AND verification_status = 'Verified';
                """;
            operationCommand.Parameters.AddWithValue("$operationId", backup.OperationId.ToString("D"));
            operationCommand.Parameters.AddWithValue("$expectedUpdatedUtc", FormatDate(expectedOperationUpdatedUtc));
            operationCommand.Parameters.AddWithValue("$updatedUtc", FormatDate(backup.DateUpdatedUtc));
            if (await operationCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO replacement_backups (
                operation_id, backup_path, status, source_size, bytes_copied,
                sha256, failure_message, date_created_utc, date_updated_utc)
            VALUES (
                $operationId, $backupPath, $status, $sourceSize, $bytesCopied,
                $sha256, $failureMessage, $dateCreatedUtc, $dateUpdatedUtc)
            ON CONFLICT(operation_id) DO UPDATE SET
                backup_path = excluded.backup_path,
                status = excluded.status,
                source_size = excluded.source_size,
                bytes_copied = excluded.bytes_copied,
                sha256 = excluded.sha256,
                failure_message = excluded.failure_message,
                date_created_utc = excluded.date_created_utc,
                date_updated_utc = excluded.date_updated_utc
            WHERE replacement_backups.backup_path = excluded.backup_path;
            """;
        AddParameters(command, backup);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("The existing original-backup state is not retryable.");
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateAsync(
        Guid operationId,
        OriginalBackupStatus status,
        long bytesCopied,
        string? sha256,
        string? failureMessage,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE replacement_backups
            SET status = $status,
                bytes_copied = $bytesCopied,
                sha256 = $sha256,
                failure_message = $failureMessage,
                date_updated_utc = $updatedUtc
            WHERE operation_id = $operationId;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$bytesCopied", bytesCopied);
        command.Parameters.AddWithValue("$sha256", sha256 ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$failureMessage", failureMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", FormatDate(updatedUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryCancelForCleanupAsync(
        Guid operationId,
        DateTimeOffset expectedUpdatedUtc,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE replacement_backups
            SET status = 'Cancelled',
                sha256 = NULL,
                failure_message = 'Original-backup artifact discarded by the user after explicit confirmation.',
                date_updated_utc = $updatedUtc
            WHERE operation_id = $operationId
              AND date_updated_utc = $expectedUpdatedUtc;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        command.Parameters.AddWithValue("$expectedUpdatedUtc", FormatDate(expectedUpdatedUtc));
        command.Parameters.AddWithValue("$updatedUtc", FormatDate(updatedUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<OriginalBackupState?> GetAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT operation_id, backup_path, status, source_size, bytes_copied,
                   sha256, failure_message, date_created_utc, date_updated_utc
            FROM replacement_backups
            WHERE operation_id = $operationId;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static void AddParameters(SqliteCommand command, OriginalBackupState backup)
    {
        command.Parameters.AddWithValue("$operationId", backup.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$backupPath", backup.BackupPath);
        command.Parameters.AddWithValue("$status", backup.Status.ToString());
        command.Parameters.AddWithValue("$sourceSize", backup.SourceSize);
        command.Parameters.AddWithValue("$bytesCopied", backup.BytesCopied);
        command.Parameters.AddWithValue("$sha256", backup.Sha256 ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$failureMessage", backup.FailureMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$dateCreatedUtc", FormatDate(backup.DateCreatedUtc));
        command.Parameters.AddWithValue("$dateUpdatedUtc", FormatDate(backup.DateUpdatedUtc));
    }

    private static OriginalBackupState Read(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        Enum.Parse<OriginalBackupStatus>(reader.GetString(2)),
        reader.GetInt64(3),
        reader.GetInt64(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        ParseDate(reader.GetString(7)),
        ParseDate(reader.GetString(8)));

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
