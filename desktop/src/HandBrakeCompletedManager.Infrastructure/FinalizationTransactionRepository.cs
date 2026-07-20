using System.Globalization;
using HandBrakeCompletedManager.Core;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class FinalizationTransactionRepository(string databasePath)
{
    private readonly string _databasePath = Path.GetFullPath(databasePath);

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        new CompletedEncodeRepository(_databasePath).InitializeAsync(cancellationToken);

    public async Task<bool> TryCreatePreparedAsync(
        FinalizationTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Checkpoint != FinalizationCheckpoint.Prepared || transaction.Revision != 0)
        {
            throw new ArgumentException("A new finalisation transaction must be at the prepared checkpoint and revision zero.", nameof(transaction));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO finalization_transactions (
                operation_id, checkpoint, source_sha256, final_sha256, revision,
                failure_message, date_created_utc, date_updated_utc)
            SELECT
                operation.id, $checkpoint, $sourceSha256, $finalSha256, $revision,
                NULL, $dateCreatedUtc, $dateUpdatedUtc
            FROM replacement_operations AS operation
            INNER JOIN replacement_backups AS backup ON backup.operation_id = operation.id
            WHERE operation.id = $operationId
              AND operation.status = 'InProgress'
              AND operation.stage = 'BackingUpSource'
              AND operation.verification_status = 'Verified'
              AND operation.bytes_copied = operation.destination_size
              AND backup.status = 'Verified'
              AND backup.bytes_copied = backup.source_size
              AND upper(backup.sha256) = upper($sourceSha256)
            ON CONFLICT(operation_id) DO NOTHING;
            """;
        AddParameters(command, transaction);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<FinalizationTransaction?> GetAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT operation_id, checkpoint, source_sha256, final_sha256, revision,
                   failure_message, date_created_utc, date_updated_utc
            FROM finalization_transactions
            WHERE operation_id = $operationId;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<bool> TryTransitionAsync(
        Guid operationId,
        FinalizationCheckpoint expectedCheckpoint,
        int expectedRevision,
        FinalizationCheckpoint nextCheckpoint,
        string? failureMessage,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        FinalizationStateMachine.EnsureTransition(expectedCheckpoint, nextCheckpoint);
        if (expectedRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE finalization_transactions
            SET checkpoint = $nextCheckpoint,
                revision = revision + 1,
                failure_message = $failureMessage,
                date_updated_utc = $updatedUtc
            WHERE operation_id = $operationId
              AND checkpoint = $expectedCheckpoint
              AND revision = $expectedRevision;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        command.Parameters.AddWithValue("$expectedCheckpoint", expectedCheckpoint.ToString());
        command.Parameters.AddWithValue("$expectedRevision", expectedRevision);
        command.Parameters.AddWithValue("$nextCheckpoint", nextCheckpoint.ToString());
        command.Parameters.AddWithValue("$failureMessage", failureMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", FormatDate(updatedUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryRecordFailureAsync(
        Guid operationId,
        FinalizationCheckpoint expectedCheckpoint,
        int expectedRevision,
        string failureMessage,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        if (expectedRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }

        if (string.IsNullOrWhiteSpace(failureMessage))
        {
            throw new ArgumentException("A finalisation failure message is required.", nameof(failureMessage));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE finalization_transactions
            SET revision = revision + 1,
                failure_message = $failureMessage,
                date_updated_utc = $updatedUtc
            WHERE operation_id = $operationId
              AND checkpoint = $expectedCheckpoint
              AND revision = $expectedRevision;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        command.Parameters.AddWithValue("$expectedCheckpoint", expectedCheckpoint.ToString());
        command.Parameters.AddWithValue("$expectedRevision", expectedRevision);
        command.Parameters.AddWithValue("$failureMessage", failureMessage);
        command.Parameters.AddWithValue("$updatedUtc", FormatDate(updatedUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryCompleteForwardAsync(
        Guid operationId,
        int expectedRevision,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        if (expectedRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var databaseTransaction = connection.BeginTransaction();
        await using (var journalCommand = connection.CreateCommand())
        {
            journalCommand.Transaction = databaseTransaction;
            journalCommand.CommandText = """
                UPDATE finalization_transactions
                SET checkpoint = 'Completed',
                    revision = revision + 1,
                    failure_message = NULL,
                    date_updated_utc = $updatedUtc
                WHERE operation_id = $operationId
                  AND checkpoint = 'SourceRecycled'
                  AND revision = $expectedRevision;
                """;
            journalCommand.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
            journalCommand.Parameters.AddWithValue("$expectedRevision", expectedRevision);
            journalCommand.Parameters.AddWithValue("$updatedUtc", FormatDate(updatedUtc));
            if (await journalCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await databaseTransaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var operationCommand = connection.CreateCommand())
        {
            operationCommand.Transaction = databaseTransaction;
            operationCommand.CommandText = """
                UPDATE replacement_operations
                SET status = 'Completed',
                    stage = 'Completed',
                    failure_message = NULL,
                    date_updated_utc = $updatedUtc
                WHERE id = $operationId
                  AND status = 'InProgress'
                  AND stage = 'BackingUpSource'
                  AND verification_status = 'Verified'
                  AND bytes_copied = destination_size;
                """;
            operationCommand.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
            operationCommand.Parameters.AddWithValue("$updatedUtc", FormatDate(updatedUtc));
            if (await operationCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await databaseTransaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = databaseTransaction;
            historyCommand.CommandText = """
                UPDATE completed_encodes
                SET source_exists = 0,
                    date_updated_utc = $updatedUtc
                WHERE id = (
                    SELECT completed_encode_id
                    FROM replacement_operations
                    WHERE id = $operationId);
                """;
            historyCommand.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
            historyCommand.Parameters.AddWithValue("$updatedUtc", FormatDate(updatedUtc));
            if (await historyCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await databaseTransaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await databaseTransaction.CommitAsync(cancellationToken);
        return true;
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

    private static void AddParameters(SqliteCommand command, FinalizationTransaction transaction)
    {
        command.Parameters.AddWithValue("$operationId", transaction.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$checkpoint", transaction.Checkpoint.ToString());
        command.Parameters.AddWithValue("$sourceSha256", transaction.SourceSha256);
        command.Parameters.AddWithValue("$finalSha256", transaction.FinalSha256);
        command.Parameters.AddWithValue("$revision", transaction.Revision);
        command.Parameters.AddWithValue("$dateCreatedUtc", FormatDate(transaction.DateCreatedUtc));
        command.Parameters.AddWithValue("$dateUpdatedUtc", FormatDate(transaction.DateUpdatedUtc));
    }

    private static FinalizationTransaction Read(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Enum.Parse<FinalizationCheckpoint>(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetInt32(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        ParseDate(reader.GetString(6)),
        ParseDate(reader.GetString(7)));

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
