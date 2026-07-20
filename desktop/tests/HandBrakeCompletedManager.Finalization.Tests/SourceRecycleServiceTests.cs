using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Finalization.Tests;

public sealed class SourceRecycleServiceTests
{
    [Fact]
    public async Task RecycleAsync_RecyclesVerifiedSourceAndPreservesBackupAndFinal()
    {
        using var fixture = await RecycleFixture.CreateAsync();
        var recycler = new TestRecycler();

        var result = await fixture.CreateService(recycler).RecycleAsync(fixture.Operation.Id);
        var transaction = await fixture.TransactionRepository.GetAsync(fixture.Operation.Id);

        Assert.False(result.WasRecovered);
        Assert.Equal(1, recycler.CallCount);
        Assert.False(File.Exists(fixture.Operation.SourcePath));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.BackupPath));
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
        Assert.Equal(FinalizationCheckpoint.SourceRecycled, transaction?.Checkpoint);
        Assert.Null(transaction?.FailureMessage);
    }

    [Fact]
    public async Task RecycleAsync_RefusesTamperedSourceBeforeRecordingIntent()
    {
        using var fixture = await RecycleFixture.CreateAsync();
        var recycler = new TestRecycler();
        await File.WriteAllBytesAsync(
            fixture.Operation.SourcePath,
            Enumerable.Repeat((byte)45, fixture.SourceBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreateService(recycler).RecycleAsync(fixture.Operation.Id));

        Assert.Equal(0, recycler.CallCount);
        Assert.True(File.Exists(fixture.Operation.SourcePath));
        Assert.Equal(FinalizationCheckpoint.FinalPromoted, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task RecycleAsync_RefusesTamperedBackupBeforeRecordingIntent()
    {
        using var fixture = await RecycleFixture.CreateAsync();
        var recycler = new TestRecycler();
        await File.WriteAllBytesAsync(
            fixture.Operation.BackupPath,
            Enumerable.Repeat((byte)46, fixture.SourceBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreateService(recycler).RecycleAsync(fixture.Operation.Id));

        Assert.Equal(0, recycler.CallCount);
        Assert.True(File.Exists(fixture.Operation.SourcePath));
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
        Assert.Equal(FinalizationCheckpoint.FinalPromoted, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task RecycleAsync_RefusesTamperedFinalBeforeRecordingIntent()
    {
        using var fixture = await RecycleFixture.CreateAsync();
        var recycler = new TestRecycler();
        await File.WriteAllBytesAsync(
            fixture.Operation.FinalPath,
            Enumerable.Repeat((byte)47, fixture.FinalBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreateService(recycler).RecycleAsync(fixture.Operation.Id));

        Assert.Equal(0, recycler.CallCount);
        Assert.True(File.Exists(fixture.Operation.SourcePath));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.BackupPath));
        Assert.Equal(FinalizationCheckpoint.FinalPromoted, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task RecycleAsync_RetriesAfterIntentWasPersisted()
    {
        using var fixture = await RecycleFixture.CreateAsync();
        var recycler = new TestRecycler();
        var fault = new ThrowingFaultInjector(SourceRecycleFaultPoint.AfterIntentPersisted);

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.CreateService(recycler, fault).RecycleAsync(fixture.Operation.Id));
        Assert.Equal(0, recycler.CallCount);
        Assert.True(File.Exists(fixture.Operation.SourcePath));
        Assert.Equal(
            FinalizationCheckpoint.RecycleSourceIntentRecorded,
            (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);

        var result = await fixture.CreateService(recycler).RecycleAsync(fixture.Operation.Id);
        Assert.False(result.WasRecovered);
        Assert.Equal(1, recycler.CallCount);
        Assert.False(File.Exists(fixture.Operation.SourcePath));
    }

    [Fact]
    public async Task RecycleAsync_RecordsCompletedRecycleAfterInterruption()
    {
        using var fixture = await RecycleFixture.CreateAsync();
        var recycler = new TestRecycler();
        var fault = new ThrowingFaultInjector(SourceRecycleFaultPoint.AfterRecycleCompleted);

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.CreateService(recycler, fault).RecycleAsync(fixture.Operation.Id));
        Assert.Equal(1, recycler.CallCount);
        Assert.False(File.Exists(fixture.Operation.SourcePath));
        Assert.Equal(
            FinalizationCheckpoint.RecycleSourceIntentRecorded,
            (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);

        var result = await fixture.CreateService(recycler).RecycleAsync(fixture.Operation.Id);
        Assert.True(result.WasRecovered);
        Assert.Equal(1, recycler.CallCount);
        Assert.Equal(FinalizationCheckpoint.SourceRecycled, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.BackupPath));
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
    }

    [Fact]
    public async Task RecycleAsync_RecordsRecyclerFailureAndLeavesSourcePresent()
    {
        using var fixture = await RecycleFixture.CreateAsync();
        var recycler = new TestRecycler(new IOException("Recycle Bin is unavailable."));

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.CreateService(recycler).RecycleAsync(fixture.Operation.Id));

        var transaction = await fixture.TransactionRepository.GetAsync(fixture.Operation.Id);
        Assert.Equal(1, recycler.CallCount);
        Assert.True(File.Exists(fixture.Operation.SourcePath));
        Assert.Equal(FinalizationCheckpoint.RecycleSourceIntentRecorded, transaction?.Checkpoint);
        Assert.Contains("Recycle Bin is unavailable", transaction?.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.BackupPath));
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
    }

    [Fact]
    public async Task RecycleAsync_RefusesLockedSourceBeforeRecordingIntent()
    {
        using var fixture = await RecycleFixture.CreateAsync();
        var recycler = new TestRecycler();
        using var sourceLock = new FileStream(
            fixture.Operation.SourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.CreateService(recycler).RecycleAsync(fixture.Operation.Id));

        Assert.Equal(0, recycler.CallCount);
        Assert.Equal(FinalizationCheckpoint.FinalPromoted, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task RecycleAsync_DoesNotTreatMissingSourceAsCompletedWithoutIntent()
    {
        using var fixture = await RecycleFixture.CreateAsync();
        var recycler = new TestRecycler();
        File.Delete(fixture.Operation.SourcePath);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            fixture.CreateService(recycler).RecycleAsync(fixture.Operation.Id));

        Assert.Equal(0, recycler.CallCount);
        Assert.Equal(FinalizationCheckpoint.FinalPromoted, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    private sealed class TestRecycler(Exception? failure = null) : IRecoverableFileRecycler
    {
        public int CallCount { get; private set; }

        public Task RecycleAsync(string path, CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (failure is not null) return Task.FromException(failure);
            File.Delete(path);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingFaultInjector(SourceRecycleFaultPoint target) : ISourceRecycleFaultInjector
    {
        public Task OnFaultPointAsync(SourceRecycleFaultPoint faultPoint) =>
            faultPoint == target
                ? Task.FromException(new IOException($"Injected interruption at {faultPoint}."))
                : Task.CompletedTask;
    }

    private sealed class RecycleFixture : IDisposable
    {
        private RecycleFixture(
            string directory,
            ReplacementOperation operation,
            byte[] sourceBytes,
            byte[] finalBytes,
            ReplacementOperationRepository operationRepository,
            FinalizationTransactionRepository transactionRepository)
        {
            Directory = directory;
            Operation = operation;
            SourceBytes = sourceBytes;
            FinalBytes = finalBytes;
            OperationRepository = operationRepository;
            TransactionRepository = transactionRepository;
        }

        public string Directory { get; }
        public ReplacementOperation Operation { get; }
        public byte[] SourceBytes { get; }
        public byte[] FinalBytes { get; }
        public ReplacementOperationRepository OperationRepository { get; }
        public FinalizationTransactionRepository TransactionRepository { get; }

        public SourceRecycleService CreateService(
            IRecoverableFileRecycler recycler,
            ISourceRecycleFaultInjector? faultInjector = null) =>
            new(OperationRepository, TransactionRepository, recycler, faultInjector);

        public static async Task<RecycleFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"hbcm-source-recycle-{Guid.NewGuid():N}");
            var sourceDirectory = Path.Combine(directory, "source");
            var convertedDirectory = Path.Combine(directory, "converted");
            System.IO.Directory.CreateDirectory(sourceDirectory);
            System.IO.Directory.CreateDirectory(convertedDirectory);
            var sourcePath = Path.Combine(sourceDirectory, "video.mkv");
            var destinationPath = Path.Combine(convertedDirectory, "video.mp4");
            var finalPath = Path.Combine(sourceDirectory, "video.mp4");
            var temporaryPath = Path.Combine(sourceDirectory, "video.mp4.hbcm-copying");
            var backupPath = Path.Combine(sourceDirectory, "HandBrake Original Backup", "video.mkv");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            var sourceBytes = Enumerable.Range(0, 10_240).Select(value => (byte)(value % 251)).ToArray();
            var finalBytes = Enumerable.Range(0, 5_120).Select(value => (byte)(value % 239)).ToArray();
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            await File.WriteAllBytesAsync(destinationPath, finalBytes);
            await File.WriteAllBytesAsync(backupPath, sourceBytes);
            await File.WriteAllBytesAsync(finalPath, finalBytes);

            var databasePath = Path.Combine(directory, "history.db");
            var completedRepository = new CompletedEncodeRepository(databasePath);
            var operationRepository = new ReplacementOperationRepository(databasePath);
            var backupRepository = new OriginalBackupRepository(databasePath);
            var transactionRepository = new FinalizationTransactionRepository(databasePath);
            var now = DateTimeOffset.UtcNow;
            var encode = new CompletedEncode(
                Guid.NewGuid(), $"RECYCLE-{Guid.NewGuid():N}", now,
                sourcePath, "video.mkv", ".mkv", sourceBytes.Length, true,
                destinationPath, "video.mp4", ".mp4", finalBytes.Length, true,
                File.GetLastWriteTimeUtc(destinationPath), 50, 50, sourceBytes.Length - finalBytes.Length,
                0, "Completed", now, now);
            var operation = new ReplacementOperation(
                Guid.NewGuid(), encode.Id, ReplacementOperationStatus.InProgress,
                ReplacementOperationStage.Verifying,
                sourcePath, destinationPath, finalPath, temporaryPath, backupPath,
                sourceBytes.Length, finalBytes.Length, finalBytes.Length,
                ReplacementVerificationStatus.Verified, null, now, now);
            var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
            var finalHash = Convert.ToHexString(SHA256.HashData(finalBytes));
            var backup = new OriginalBackupState(
                operation.Id, backupPath, OriginalBackupStatus.Verified,
                sourceBytes.Length, sourceBytes.Length, sourceHash, null, now, now);

            await completedRepository.InitializeAsync();
            Assert.True(await completedRepository.AddAsync(encode));
            await operationRepository.AddAsync(operation);
            Assert.True(await backupRepository.TryBeginAsync(backup, now));
            Assert.True(await backupRepository.UpdateAsync(
                operation.Id, OriginalBackupStatus.Verified, sourceBytes.Length,
                sourceHash, null, now.AddSeconds(1)));
            var readyOperation = operation with { Stage = ReplacementOperationStage.BackingUpSource };
            await new FinalizationPreparationService(transactionRepository).PrepareAsync(
                readyOperation,
                new FinalizationReadinessResult(true, finalHash, sourceHash, []));
            Assert.True(await transactionRepository.TryTransitionAsync(
                operation.Id, FinalizationCheckpoint.Prepared, 0,
                FinalizationCheckpoint.PromoteTemporaryIntentRecorded, null, now.AddSeconds(2)));
            Assert.True(await transactionRepository.TryTransitionAsync(
                operation.Id, FinalizationCheckpoint.PromoteTemporaryIntentRecorded, 1,
                FinalizationCheckpoint.FinalPromoted, null, now.AddSeconds(3)));

            return new RecycleFixture(
                directory, readyOperation, sourceBytes, finalBytes,
                operationRepository, transactionRepository);
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
