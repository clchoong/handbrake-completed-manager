using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class CompletedEncodeRepositoryTests
{
    [Fact]
    public async Task InitializeAsync_UpgradesExistingReplacementSchemaThroughFinalizationJournal()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "handbrake-completed-manager-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, "history.db");

        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var assembly = typeof(CompletedEncodeRepository).Assembly;
                foreach (var resourceName in new[]
                         {
                             "HandBrakeCompletedManager.Infrastructure.Migrations.001_initial.sql",
                             "HandBrakeCompletedManager.Infrastructure.Migrations.002_replacement_operations.sql"
                         })
                {
                    await using var stream = assembly.GetManifestResourceStream(resourceName)
                        ?? throw new InvalidOperationException($"Migration resource is missing: {resourceName}");
                    using var migrationReader = new StreamReader(stream);
                    await using var migrationCommand = connection.CreateCommand();
                    migrationCommand.CommandText = await migrationReader.ReadToEndAsync();
                    await migrationCommand.ExecuteNonQueryAsync();
                }
            }

            await new CompletedEncodeRepository(databasePath).InitializeAsync();

            await using var verificationConnection = new SqliteConnection($"Data Source={databasePath}");
            await verificationConnection.OpenAsync();
            await using var verificationCommand = verificationConnection.CreateCommand();
            verificationCommand.CommandText = """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'replacement_operations';
                """;
            Assert.Equal(1L, await verificationCommand.ExecuteScalarAsync());
            verificationCommand.CommandText = """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'finalization_transactions';
                """;
            Assert.Equal(1L, await verificationCommand.ExecuteScalarAsync());
            verificationCommand.CommandText = """
                SELECT sql
                FROM sqlite_master
                WHERE type = 'index' AND name = 'ux_replacement_operations_active_encode';
                """;
            var retryIndexSql = Assert.IsType<string>(await verificationCommand.ExecuteScalarAsync());
            Assert.Contains("Planned", retryIndexSql);
            Assert.Contains("InProgress", retryIndexSql);
            Assert.DoesNotContain("Failed", retryIndexSql);
            verificationCommand.CommandText = """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'replacement_backups';
                """;
            Assert.Equal(1L, await verificationCommand.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AddAsync_PreventsDuplicateFingerprintAndPersistsRecord()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "handbrake-completed-manager-tests",
            Guid.NewGuid().ToString("N"));
        var repository = new CompletedEncodeRepository(Path.Combine(testDirectory, "history.db"));
        var completedAt = new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);
        var record = new CompletedEncode(
            Guid.NewGuid(),
            "FINGERPRINT",
            completedAt,
            @"D:\Videos\Source.mkv",
            "Source.mkv",
            ".mkv",
            1_000,
            true,
            @"E:\Converted\Output.mp4",
            "Output.mp4",
            ".mp4",
            400,
            true,
            completedAt,
            40,
            60,
            600,
            0,
            "Completed",
            completedAt,
            completedAt);

        try
        {
            await repository.InitializeAsync();

            var firstInsert = await repository.AddAsync(record);
            var duplicateInsert = await repository.AddAsync(record with { Id = Guid.NewGuid() });
            var records = await repository.GetAllAsync();

            Assert.True(firstInsert);
            Assert.False(duplicateInsert);
            var persisted = Assert.Single(records);
            Assert.Equal(record.SourcePath, persisted.SourcePath);
            Assert.Equal(record.SpaceSavedBytes, persisted.SpaceSavedBytes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RemoveFromHistoryAsync_RemovesOnlyRecordAndLeavesFilesUntouched()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "handbrake-completed-manager-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var sourcePath = Path.Combine(testDirectory, "Source.mkv");
        var destinationPath = Path.Combine(testDirectory, "Output.mp4");
        await File.WriteAllTextAsync(sourcePath, "source-content");
        await File.WriteAllTextAsync(destinationPath, "output-content");
        var repository = new CompletedEncodeRepository(Path.Combine(testDirectory, "history.db"));
        var operationRepository = new ReplacementOperationRepository(Path.Combine(testDirectory, "history.db"));
        var completedAt = new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);
        var record = new CompletedEncode(
            Guid.NewGuid(),
            "REMOVE-FINGERPRINT",
            completedAt,
            sourcePath,
            Path.GetFileName(sourcePath),
            Path.GetExtension(sourcePath),
            new FileInfo(sourcePath).Length,
            true,
            destinationPath,
            Path.GetFileName(destinationPath),
            Path.GetExtension(destinationPath),
            new FileInfo(destinationPath).Length,
            true,
            completedAt,
            50,
            50,
            7,
            0,
            "Completed",
            completedAt,
            completedAt);

        try
        {
            await repository.InitializeAsync();
            Assert.True(await repository.AddAsync(record));
            var operation = new ReplacementOperation(
                Guid.NewGuid(),
                record.Id,
                ReplacementOperationStatus.Completed,
                ReplacementOperationStage.Completed,
                record.SourcePath,
                record.DestinationPath,
                Path.ChangeExtension(record.SourcePath, record.DestinationExtension),
                Path.ChangeExtension(record.SourcePath, record.DestinationExtension) + ".hbcm-copying",
                record.SourcePath + ".backup",
                record.SourceSize!.Value,
                record.DestinationSize!.Value,
                record.DestinationSize.Value,
                ReplacementVerificationStatus.Verified,
                null,
                completedAt,
                completedAt);
            await operationRepository.AddAsync(operation);
            await repository.UpsertFileActionAsync(
                record.Id,
                Path.ChangeExtension(record.SourcePath, record.DestinationExtension),
                "Source Replaced",
                false,
                completedAt);

            var removed = await repository.RemoveFromHistoryAsync(record.Id);
            var removedAgain = await repository.RemoveFromHistoryAsync(record.Id);
            var records = await repository.GetAllAsync();

            Assert.True(removed);
            Assert.False(removedAgain);
            Assert.Empty(records);
            Assert.Null(await operationRepository.GetByIdAsync(operation.Id));
            Assert.True(File.Exists(sourcePath));
            Assert.True(File.Exists(destinationPath));
            Assert.Equal("source-content", await File.ReadAllTextAsync(sourcePath));
            Assert.Equal("output-content", await File.ReadAllTextAsync(destinationPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }
}
