using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class OutputRecycleServiceTests
{
    [Fact]
    public async Task RecycleAsync_RecyclesVerifiedOutputAndKeepsSourceAndHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recycler = new DeletingRecycler();
        var service = fixture.CreateService(recycler);

        var result = await service.RecycleAsync(fixture.Record);
        var persisted = Assert.Single(await fixture.CompletedEncodes.GetAllAsync());

        Assert.Equal(fixture.Record.Id, result.CompletedEncodeId);
        Assert.False(File.Exists(fixture.OutputPath));
        Assert.True(File.Exists(fixture.SourcePath));
        Assert.False(persisted.DestinationExists);
        Assert.Equal(1, recycler.CallCount);
    }

    [Fact]
    public async Task RecycleAsync_RefusesChangedOutput()
    {
        await using var fixture = await Fixture.CreateAsync();
        await File.AppendAllTextAsync(fixture.OutputPath, "changed");
        var recycler = new DeletingRecycler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.CreateService(recycler).RecycleAsync(fixture.Record));

        Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(fixture.OutputPath));
        Assert.Equal(0, recycler.CallCount);
    }

    [Fact]
    public async Task RecycleAsync_RefusesWhileReplacementIsUnfinished()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operation = fixture.CreateReplacementOperation(ReplacementOperationStatus.Planned);
        await fixture.ReplacementOperations.AddAsync(operation);
        var recycler = new DeletingRecycler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.CreateService(recycler).RecycleAsync(fixture.Record));

        Assert.Contains("unfinished replacement", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(fixture.OutputPath));
        Assert.Equal(0, recycler.CallCount);
    }

    [Fact]
    public async Task RecycleAsync_LeavesHistoryAvailableWhenRecyclerFails()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CreateService(new FailingRecycler());

        await Assert.ThrowsAsync<IOException>(() => service.RecycleAsync(fixture.Record));
        var persisted = Assert.Single(await fixture.CompletedEncodes.GetAllAsync());

        Assert.True(File.Exists(fixture.OutputPath));
        Assert.True(persisted.DestinationExists);
    }

    [Fact]
    public async Task LatestSourceReplacementStates_ReportsCompletedAndUndoneCheckpoints()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operation = fixture.CreateReplacementOperation(ReplacementOperationStatus.Completed);
        await fixture.ReplacementOperations.AddAsync(operation);

        await using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO finalization_transactions (
                    operation_id, checkpoint, source_sha256, final_sha256, revision,
                    failure_message, date_created_utc, date_updated_utc)
                VALUES ($operationId, 'Completed', $sourceSha256, $finalSha256, 1, NULL, $created, $updated);
                """;
            command.Parameters.AddWithValue("$operationId", operation.Id.ToString("D"));
            command.Parameters.AddWithValue("$sourceSha256", new string('A', 64));
            command.Parameters.AddWithValue("$finalSha256", new string('B', 64));
            command.Parameters.AddWithValue("$created", operation.DateCreatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$updated", operation.DateUpdatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        var completedStates = await fixture.ReplacementOperations.GetLatestSourceReplacementStatesAsync();
        Assert.Equal(SourceReplacementState.Replaced, completedStates[fixture.Record.Id]);

        await using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE finalization_transactions SET checkpoint = 'Undone' WHERE operation_id = $operationId;";
            command.Parameters.AddWithValue("$operationId", operation.Id.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        var undoneStates = await fixture.ReplacementOperations.GetLatestSourceReplacementStatesAsync();
        Assert.Equal(SourceReplacementState.Restored, undoneStates[fixture.Record.Id]);
    }

    private sealed class DeletingRecycler : IRecoverableFileRecycler
    {
        public int CallCount { get; private set; }

        public Task RecycleAsync(string path, CancellationToken cancellationToken = default)
        {
            CallCount++;
            File.Delete(path);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingRecycler : IRecoverableFileRecycler
    {
        public Task RecycleAsync(string path, CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated Recycle Bin failure.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            string directory,
            string databasePath,
            string sourcePath,
            string outputPath,
            CompletedEncode record)
        {
            Directory = directory;
            DatabasePath = databasePath;
            SourcePath = sourcePath;
            OutputPath = outputPath;
            Record = record;
            CompletedEncodes = new CompletedEncodeRepository(databasePath);
            ReplacementOperations = new ReplacementOperationRepository(databasePath);
            FinalizationTransactions = new FinalizationTransactionRepository(databasePath);
        }

        public string Directory { get; }
        public string DatabasePath { get; }
        public string SourcePath { get; }
        public string OutputPath { get; }
        public CompletedEncode Record { get; }
        public CompletedEncodeRepository CompletedEncodes { get; }
        public ReplacementOperationRepository ReplacementOperations { get; }
        public FinalizationTransactionRepository FinalizationTransactions { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "handbrake-completed-manager-tests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "source.mkv");
            var outputPath = Path.Combine(directory, "output.mp4");
            await File.WriteAllTextAsync(sourcePath, "original source");
            await File.WriteAllTextAsync(outputPath, "encoded output");
            var now = DateTimeOffset.UtcNow;
            var output = new FileInfo(outputPath);
            var record = new CompletedEncode(
                Guid.NewGuid(),
                Guid.NewGuid().ToString("N"),
                now,
                sourcePath,
                Path.GetFileName(sourcePath),
                Path.GetExtension(sourcePath),
                new FileInfo(sourcePath).Length,
                true,
                outputPath,
                Path.GetFileName(outputPath),
                Path.GetExtension(outputPath),
                output.Length,
                true,
                output.LastWriteTimeUtc,
                50,
                50,
                10,
                0,
                "Completed",
                now,
                now);
            var fixture = new Fixture(
                directory,
                Path.Combine(directory, "history.db"),
                sourcePath,
                outputPath,
                record);
            await fixture.CompletedEncodes.InitializeAsync();
            await fixture.CompletedEncodes.AddAsync(record);
            return fixture;
        }

        public OutputRecycleService CreateService(IRecoverableFileRecycler recycler) => new(
            CompletedEncodes,
            ReplacementOperations,
            FinalizationTransactions,
            recycler);

        public ReplacementOperation CreateReplacementOperation(ReplacementOperationStatus status)
        {
            var now = DateTimeOffset.UtcNow;
            return new ReplacementOperation(
                Guid.NewGuid(),
                Record.Id,
                status,
                status == ReplacementOperationStatus.Completed
                    ? ReplacementOperationStage.Completed
                    : ReplacementOperationStage.Preparing,
                SourcePath,
                OutputPath,
                Path.Combine(Path.GetDirectoryName(SourcePath)!, "source.mp4"),
                Path.Combine(Path.GetDirectoryName(SourcePath)!, ".source.hbcm.tmp"),
                Path.Combine(Path.GetDirectoryName(SourcePath)!, ".source.hbcm.backup"),
                Record.SourceSize!.Value,
                Record.DestinationSize!.Value,
                status == ReplacementOperationStatus.Completed ? Record.DestinationSize.Value : 0,
                status == ReplacementOperationStatus.Completed
                    ? ReplacementVerificationStatus.Verified
                    : ReplacementVerificationStatus.NotVerified,
                null,
                now,
                now);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
