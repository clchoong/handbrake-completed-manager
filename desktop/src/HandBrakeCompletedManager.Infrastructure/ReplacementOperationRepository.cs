using System.Globalization;
using HandBrakeCompletedManager.Core;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class ReplacementOperationRepository(string databasePath)
{
    private readonly string _databasePath = Path.GetFullPath(databasePath);

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        new CompletedEncodeRepository(_databasePath).InitializeAsync(cancellationToken);

    public async Task AddAsync(
        ReplacementOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO replacement_operations (
                id, completed_encode_id, status, stage,
                source_path, destination_path, final_path, temporary_path, backup_path,
                source_size, destination_size, bytes_copied, verification_status,
                failure_message, date_created_utc, date_updated_utc)
            VALUES (
                $id, $completedEncodeId, $status, $stage,
                $sourcePath, $destinationPath, $finalPath, $temporaryPath, $backupPath,
                $sourceSize, $destinationSize, $bytesCopied, $verificationStatus,
                $failureMessage, $dateCreatedUtc, $dateUpdatedUtc);
            """;
        AddParameters(command, operation);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> UpdateStateAsync(
        Guid operationId,
        ReplacementOperationStatus status,
        ReplacementOperationStage stage,
        long bytesCopied,
        ReplacementVerificationStatus verificationStatus,
        string? failureMessage,
        DateTimeOffset dateUpdatedUtc,
        CancellationToken cancellationToken = default)
    {
        if (bytesCopied < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesCopied));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE replacement_operations
            SET status = $status,
                stage = $stage,
                bytes_copied = $bytesCopied,
                verification_status = $verificationStatus,
                failure_message = $failureMessage,
                date_updated_utc = $dateUpdatedUtc
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$stage", stage.ToString());
        command.Parameters.AddWithValue("$bytesCopied", bytesCopied);
        command.Parameters.AddWithValue("$verificationStatus", verificationStatus.ToString());
        command.Parameters.AddWithValue("$failureMessage", failureMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$dateUpdatedUtc", FormatDate(dateUpdatedUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<ReplacementOperation>> GetIncompleteAsync(
        CancellationToken cancellationToken = default)
    {
        var operations = new List<ReplacementOperation>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, completed_encode_id, status, stage,
                   source_path, destination_path, final_path, temporary_path, backup_path,
                   source_size, destination_size, bytes_copied, verification_status,
                   failure_message, date_created_utc, date_updated_utc
            FROM replacement_operations
            WHERE status NOT IN ('Completed', 'Cancelled')
            ORDER BY date_updated_utc DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            operations.Add(ReadOperation(reader));
        }

        return operations;
    }

    public async Task<ReplacementOperation?> GetByIdAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, completed_encode_id, status, stage,
                   source_path, destination_path, final_path, temporary_path, backup_path,
                   source_size, destination_size, bytes_copied, verification_status,
                   failure_message, date_created_utc, date_updated_utc
            FROM replacement_operations
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadOperation(reader) : null;
    }

    public async Task<ReplacementOperation?> GetLatestForCompletedEncodeAsync(
        Guid completedEncodeId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, completed_encode_id, status, stage,
                   source_path, destination_path, final_path, temporary_path, backup_path,
                   source_size, destination_size, bytes_copied, verification_status,
                   failure_message, date_created_utc, date_updated_utc
            FROM replacement_operations
            WHERE completed_encode_id = $completedEncodeId
            ORDER BY date_updated_utc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$completedEncodeId", completedEncodeId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadOperation(reader) : null;
    }

    public async Task<bool> TryCancelForTemporaryCleanupAsync(
        Guid operationId,
        DateTimeOffset expectedUpdatedUtc,
        long bytesCopied,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE replacement_operations
            SET status = 'Cancelled',
                stage = 'Cancelled',
                bytes_copied = $bytesCopied,
                verification_status = 'NotVerified',
                failure_message = 'Temporary copy discarded by the user after explicit confirmation.',
                date_updated_utc = $updatedUtc
            WHERE id = $id
              AND date_updated_utc = $expectedUpdatedUtc
              AND status <> 'Completed';
            """;
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$expectedUpdatedUtc", FormatDate(expectedUpdatedUtc));
        command.Parameters.AddWithValue("$bytesCopied", bytesCopied);
        command.Parameters.AddWithValue("$updatedUtc", FormatDate(updatedUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryReturnToVerifiedTemporaryAsync(
        Guid operationId,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE replacement_operations
            SET stage = 'Verifying',
                failure_message = NULL,
                date_updated_utc = $updatedUtc
            WHERE id = $id
              AND status = 'InProgress'
              AND stage = 'BackingUpSource'
              AND verification_status = 'Verified';
            """;
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$updatedUtc", FormatDate(updatedUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
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
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static void AddParameters(SqliteCommand command, ReplacementOperation operation)
    {
        command.Parameters.AddWithValue("$id", operation.Id.ToString("D"));
        command.Parameters.AddWithValue("$completedEncodeId", operation.CompletedEncodeId.ToString("D"));
        command.Parameters.AddWithValue("$status", operation.Status.ToString());
        command.Parameters.AddWithValue("$stage", operation.Stage.ToString());
        command.Parameters.AddWithValue("$sourcePath", operation.SourcePath);
        command.Parameters.AddWithValue("$destinationPath", operation.DestinationPath);
        command.Parameters.AddWithValue("$finalPath", operation.FinalPath);
        command.Parameters.AddWithValue("$temporaryPath", operation.TemporaryPath);
        command.Parameters.AddWithValue("$backupPath", operation.BackupPath);
        command.Parameters.AddWithValue("$sourceSize", operation.SourceSize);
        command.Parameters.AddWithValue("$destinationSize", operation.DestinationSize);
        command.Parameters.AddWithValue("$bytesCopied", operation.BytesCopied);
        command.Parameters.AddWithValue("$verificationStatus", operation.VerificationStatus.ToString());
        command.Parameters.AddWithValue("$failureMessage", operation.FailureMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$dateCreatedUtc", FormatDate(operation.DateCreatedUtc));
        command.Parameters.AddWithValue("$dateUpdatedUtc", FormatDate(operation.DateUpdatedUtc));
    }

    private static ReplacementOperation ReadOperation(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Enum.Parse<ReplacementOperationStatus>(reader.GetString(2)),
        Enum.Parse<ReplacementOperationStage>(reader.GetString(3)),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetInt64(9),
        reader.GetInt64(10),
        reader.GetInt64(11),
        Enum.Parse<ReplacementVerificationStatus>(reader.GetString(12)),
        reader.IsDBNull(13) ? null : reader.GetString(13),
        ParseDate(reader.GetString(14)),
        ParseDate(reader.GetString(15)));

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
