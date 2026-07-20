using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Finalization.Tests;

public sealed class SafeReplacementServiceTests
{
    [Fact]
    public async Task ReplaceAsync_CompletesEntireGuardedWorkflowAfterOneCall()
    {
        using var fixture = await SafeReplacementFixture.CreateAsync();
        var progress = new RecordingProgress();

        var result = await fixture.CreateService(new TestRecycler()).ReplaceAsync(fixture.Plan, progress);

        Assert.Equal(fixture.Plan.Paths.FinalPath, result.FinalPath);
        Assert.False(File.Exists(fixture.Plan.CompletedEncode.SourcePath));
        Assert.Equal(fixture.ConvertedBytes, await File.ReadAllBytesAsync(fixture.Plan.Paths.FinalPath));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Plan.Paths.BackupPath));
        Assert.False(File.Exists(fixture.Plan.Paths.TemporaryPath));
        Assert.Equal(fixture.ConvertedBytes, await File.ReadAllBytesAsync(fixture.Plan.CompletedEncode.DestinationPath));
        Assert.False(Assert.Single(await fixture.CompletedRepository.GetAllAsync()).SourceExists);

        var operation = await fixture.OperationRepository.GetByIdAsync(result.OperationId);
        var transaction = await fixture.TransactionRepository.GetAsync(result.OperationId);
        Assert.Equal(ReplacementOperationStatus.Completed, operation?.Status);
        Assert.Equal(ReplacementOperationStage.Completed, operation?.Stage);
        Assert.Equal(FinalizationCheckpoint.Completed, transaction?.Checkpoint);
        Assert.Equal(
            Enum.GetValues<SafeReplacementStage>(),
            progress.Items.Select(item => item.Stage).Distinct().ToArray());
    }

    [Fact]
    public async Task ReplaceAsync_RecycleBinFailureStopsAtRecoverableIntentWithAllCopiesAvailable()
    {
        using var fixture = await SafeReplacementFixture.CreateAsync();
        var recycler = new TestRecycler(new IOException("Recycle Bin unavailable."));

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            fixture.CreateService(recycler).ReplaceAsync(fixture.Plan));

        Assert.Contains("Recycle Bin unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, recycler.CallCount);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Plan.CompletedEncode.SourcePath));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Plan.Paths.BackupPath));
        Assert.Equal(fixture.ConvertedBytes, await File.ReadAllBytesAsync(fixture.Plan.Paths.FinalPath));
        Assert.False(File.Exists(fixture.Plan.Paths.TemporaryPath));
        var operation = await fixture.OperationRepository.GetLatestForCompletedEncodeAsync(
            fixture.Plan.CompletedEncode.Id);
        Assert.NotNull(operation);
        var transaction = await fixture.TransactionRepository.GetAsync(operation.Id);
        Assert.Equal(FinalizationCheckpoint.RecycleSourceIntentRecorded, transaction?.Checkpoint);
        Assert.Contains("Recycle Bin unavailable", transaction?.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(Assert.Single(await fixture.CompletedRepository.GetAllAsync()).SourceExists);
        Assert.Single(await fixture.CreateRecoveryService().ReviewAsync());
    }

    [Fact]
    public async Task ReplaceAsync_SameExtensionAtomicallyReplacesOriginalPath()
    {
        using var fixture = await SafeReplacementFixture.CreateAsync(sameExtension: true);
        var recycler = new TestRecycler();

        var result = await fixture.CreateService(recycler).ReplaceAsync(fixture.Plan);

        Assert.Equal(fixture.Plan.CompletedEncode.SourcePath, result.FinalPath, ignoreCase: true);
        Assert.Equal(fixture.ConvertedBytes, await File.ReadAllBytesAsync(result.FinalPath));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Plan.Paths.BackupPath));
        Assert.Equal(fixture.ConvertedBytes, await File.ReadAllBytesAsync(fixture.Plan.CompletedEncode.DestinationPath));
        Assert.False(File.Exists(fixture.Plan.Paths.TemporaryPath));
        Assert.Equal(0, recycler.CallCount);

        var operation = await fixture.OperationRepository.GetByIdAsync(result.OperationId);
        var transaction = await fixture.TransactionRepository.GetAsync(result.OperationId);
        Assert.Equal(ReplacementOperationStatus.Completed, operation?.Status);
        Assert.Equal(FinalizationCheckpoint.Completed, transaction?.Checkpoint);
    }

    private sealed class RecordingProgress : IProgress<SafeReplacementProgress>
    {
        public List<SafeReplacementProgress> Items { get; } = [];

        public void Report(SafeReplacementProgress value) => Items.Add(value);
    }

    private sealed class TestRecycler(Exception? failure = null) : IRecoverableFileRecycler
    {
        public int CallCount { get; private set; }

        public Task RecycleAsync(string path, CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (failure is not null)
            {
                return Task.FromException(failure);
            }

            File.Delete(path);
            return Task.CompletedTask;
        }
    }

    private sealed class UnlimitedSpaceProvider : IAvailableSpaceProvider
    {
        public long GetAvailableBytes(string path) => long.MaxValue;
    }

    private sealed class SafeReplacementFixture : IDisposable
    {
        private SafeReplacementFixture(
            string directory,
            ReplacementPlan plan,
            byte[] sourceBytes,
            byte[] convertedBytes,
            CompletedEncodeRepository completedRepository,
            ReplacementOperationRepository operationRepository,
            OriginalBackupRepository backupRepository,
            FinalizationTransactionRepository transactionRepository)
        {
            Directory = directory;
            Plan = plan;
            SourceBytes = sourceBytes;
            ConvertedBytes = convertedBytes;
            CompletedRepository = completedRepository;
            OperationRepository = operationRepository;
            BackupRepository = backupRepository;
            TransactionRepository = transactionRepository;
        }

        public string Directory { get; }
        public ReplacementPlan Plan { get; }
        public byte[] SourceBytes { get; }
        public byte[] ConvertedBytes { get; }
        public CompletedEncodeRepository CompletedRepository { get; }
        public ReplacementOperationRepository OperationRepository { get; }
        public OriginalBackupRepository BackupRepository { get; }
        public FinalizationTransactionRepository TransactionRepository { get; }

        public SafeReplacementService CreateService(IRecoverableFileRecycler recycler)
        {
            var temporaryCopy = new TemporaryCopyService(OperationRepository, new UnlimitedSpaceProvider());
            var originalBackup = new OriginalBackupService(
                OperationRepository,
                BackupRepository,
                new UnlimitedSpaceProvider());
            var preparation = new FinalizationPreparationService(TransactionRepository);
            return new SafeReplacementService(
                OperationRepository,
                BackupRepository,
                TransactionRepository,
                temporaryCopy,
                originalBackup,
                new FinalizationReadinessService(),
                preparation,
                new FinalizationPromotionService(OperationRepository, TransactionRepository),
                new SourceRecycleService(OperationRepository, TransactionRepository, recycler),
                new FinalizationCompletionService(OperationRepository, TransactionRepository));
        }

        public ReplacementRecoveryService CreateRecoveryService() =>
            new(OperationRepository, BackupRepository, TransactionRepository);

        public static async Task<SafeReplacementFixture> CreateAsync(bool sameExtension = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"hbcm-one-click-{Guid.NewGuid():N}");
            var sourceDirectory = Path.Combine(directory, "source");
            var convertedDirectory = Path.Combine(directory, "converted");
            System.IO.Directory.CreateDirectory(sourceDirectory);
            System.IO.Directory.CreateDirectory(convertedDirectory);
            var sourcePath = Path.Combine(sourceDirectory, "sample.mkv");
            var destinationExtension = sameExtension ? ".mkv" : ".mp4";
            var destinationPath = Path.Combine(convertedDirectory, $"sample{destinationExtension}");
            var sourceBytes = Enumerable.Range(0, 16_384).Select(value => (byte)(value % 251)).ToArray();
            var convertedBytes = Enumerable.Range(0, 8_192).Select(value => (byte)(value % 239)).ToArray();
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            await File.WriteAllBytesAsync(destinationPath, convertedBytes);

            var databasePath = Path.Combine(directory, "history.db");
            var completedRepository = new CompletedEncodeRepository(databasePath);
            var operationRepository = new ReplacementOperationRepository(databasePath);
            var backupRepository = new OriginalBackupRepository(databasePath);
            var transactionRepository = new FinalizationTransactionRepository(databasePath);
            var now = DateTimeOffset.UtcNow;
            var encode = new CompletedEncode(
                Guid.NewGuid(), $"ONECLICK-{Guid.NewGuid():N}", now,
                sourcePath, "sample.mkv", ".mkv", sourceBytes.Length, true,
                destinationPath, Path.GetFileName(destinationPath), destinationExtension, convertedBytes.Length, true,
                File.GetLastWriteTimeUtc(destinationPath), 50, 50,
                sourceBytes.Length - convertedBytes.Length,
                0, "Completed", now, now);
            await completedRepository.InitializeAsync();
            Assert.True(await completedRepository.AddAsync(encode));
            var plan = new ReplacementPreflightService().Review(encode);
            Assert.True(plan.CanProceed);
            return new SafeReplacementFixture(
                directory,
                plan,
                sourceBytes,
                convertedBytes,
                completedRepository,
                operationRepository,
                backupRepository,
                transactionRepository);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
