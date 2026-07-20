using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Finalization.Tests;

public sealed class UndoWorkflowServiceTests
{
    [Fact]
    public async Task PrepareAsync_RecordsUndoAndAddsRecordToRestartRecovery()
    {
        using var fixture = await UndoFixture.CreateAsync();
        var recovery = fixture.CreateRecoveryService();
        Assert.Empty(await recovery.ReviewAsync());

        var result = await fixture.CreatePreparationService().PrepareAsync(fixture.Operation.Id);

        Assert.False(result.WasAlreadyPrepared);
        Assert.Equal(FinalizationCheckpoint.UndoPrepared, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        var item = Assert.Single(await recovery.ReviewAsync());
        Assert.Contains("UndoPrepared", item.Summary, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Operation.SourcePath));
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
    }

    [Fact]
    public async Task PrepareAsync_IsIdempotentAndRefusesTamperedBackup()
    {
        using var fixture = await UndoFixture.CreateAsync();
        await fixture.CreatePreparationService().PrepareAsync(fixture.Operation.Id);
        Assert.True((await fixture.CreatePreparationService().PrepareAsync(fixture.Operation.Id)).WasAlreadyPrepared);
        await File.WriteAllBytesAsync(
            fixture.Operation.BackupPath,
            Enumerable.Repeat((byte)71, fixture.SourceBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreatePreparationService().PrepareAsync(fixture.Operation.Id));

        Assert.Equal(FinalizationCheckpoint.UndoPrepared, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.True(File.Exists(fixture.Operation.FinalPath));
    }

    [Fact]
    public async Task FullUndo_RestoresSourceRecyclesFinalAndCompletesAtomically()
    {
        using var fixture = await UndoFixture.CreateAsync();
        var recycler = new TestRecycler();
        await fixture.PrepareAndRestoreAsync();
        var recycle = await fixture.CreateFinalRecycleService(recycler).RecycleAsync(fixture.Operation.Id);
        var completion = await fixture.CreateUndoCompletionService().CompleteAsync(fixture.Operation.Id);

        Assert.False(recycle.WasRecovered);
        Assert.False(completion.WasAlreadyCompleted);
        Assert.Equal(1, recycler.CallCount);
        Assert.Equal(FinalizationCheckpoint.Undone, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.SourcePath));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.BackupPath));
        Assert.False(File.Exists(fixture.Operation.FinalPath));
        Assert.True(Assert.Single(await fixture.CompletedRepository.GetAllAsync()).SourceExists);
        Assert.Empty(await fixture.CreateRecoveryService().ReviewAsync());
    }

    [Fact]
    public async Task FinalRecycle_RetriesAfterIntentWasPersisted()
    {
        using var fixture = await UndoFixture.CreateAsync();
        await fixture.PrepareAndRestoreAsync();
        var recycler = new TestRecycler();
        var fault = new ThrowingRecycleFaultInjector(FinalFileRecycleFaultPoint.AfterIntentPersisted);

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.CreateFinalRecycleService(recycler, fault).RecycleAsync(fixture.Operation.Id));
        Assert.Equal(0, recycler.CallCount);
        Assert.True(File.Exists(fixture.Operation.FinalPath));
        Assert.Equal(FinalizationCheckpoint.RecycleFinalIntentRecorded, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);

        var result = await fixture.CreateFinalRecycleService(recycler).RecycleAsync(fixture.Operation.Id);
        Assert.False(result.WasRecovered);
        Assert.Equal(1, recycler.CallCount);
        Assert.Equal(FinalizationCheckpoint.FinalRecycled, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task FinalRecycle_RecordsCompletedRemovalAfterInterruption()
    {
        using var fixture = await UndoFixture.CreateAsync();
        await fixture.PrepareAndRestoreAsync();
        var recycler = new TestRecycler();
        var fault = new ThrowingRecycleFaultInjector(FinalFileRecycleFaultPoint.AfterRecycleCompleted);

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.CreateFinalRecycleService(recycler, fault).RecycleAsync(fixture.Operation.Id));
        Assert.False(File.Exists(fixture.Operation.FinalPath));
        Assert.Equal(FinalizationCheckpoint.RecycleFinalIntentRecorded, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);

        var result = await fixture.CreateFinalRecycleService(recycler).RecycleAsync(fixture.Operation.Id);
        Assert.True(result.WasRecovered);
        Assert.Equal(1, recycler.CallCount);
        Assert.Equal(FinalizationCheckpoint.FinalRecycled, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task FinalRecycle_RefusesTamperedRestoredSourceBeforeIntent()
    {
        using var fixture = await UndoFixture.CreateAsync();
        await fixture.PrepareAndRestoreAsync();
        await File.WriteAllBytesAsync(
            fixture.Operation.SourcePath,
            Enumerable.Repeat((byte)72, fixture.SourceBytes.Length).ToArray());
        var recycler = new TestRecycler();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreateFinalRecycleService(recycler).RecycleAsync(fixture.Operation.Id));

        Assert.Equal(0, recycler.CallCount);
        Assert.True(File.Exists(fixture.Operation.FinalPath));
        Assert.Equal(FinalizationCheckpoint.SourceRestored, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task FinalRecycle_RefusesTamperedBackupBeforeIntent()
    {
        using var fixture = await UndoFixture.CreateAsync();
        await fixture.PrepareAndRestoreAsync();
        await File.WriteAllBytesAsync(
            fixture.Operation.BackupPath,
            Enumerable.Repeat((byte)73, fixture.SourceBytes.Length).ToArray());
        var recycler = new TestRecycler();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreateFinalRecycleService(recycler).RecycleAsync(fixture.Operation.Id));

        Assert.Equal(0, recycler.CallCount);
        Assert.True(File.Exists(fixture.Operation.FinalPath));
        Assert.Equal(FinalizationCheckpoint.SourceRestored, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task UndoCompletion_RefusesTamperedSourceAndPreservesFinalRecycledCheckpoint()
    {
        using var fixture = await UndoFixture.CreateAsync();
        await fixture.PrepareRestoreAndRecycleAsync(new TestRecycler());
        await File.WriteAllBytesAsync(
            fixture.Operation.SourcePath,
            Enumerable.Repeat((byte)74, fixture.SourceBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreateUndoCompletionService().CompleteAsync(fixture.Operation.Id));

        Assert.Equal(FinalizationCheckpoint.FinalRecycled, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.False(Assert.Single(await fixture.CompletedRepository.GetAllAsync()).SourceExists);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.BackupPath));
    }

    [Fact]
    public async Task UndoCompletion_IsIdempotent()
    {
        using var fixture = await UndoFixture.CreateAsync();
        await fixture.PrepareRestoreAndRecycleAsync(new TestRecycler());
        await fixture.CreateUndoCompletionService().CompleteAsync(fixture.Operation.Id);

        var result = await fixture.CreateUndoCompletionService().CompleteAsync(fixture.Operation.Id);

        Assert.True(result.WasAlreadyCompleted);
        Assert.True(Assert.Single(await fixture.CompletedRepository.GetAllAsync()).SourceExists);
        Assert.Equal(FinalizationCheckpoint.Undone, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task TryCompleteUndoAsync_RollsBackJournalWhenCompletedOperationBoundaryChanged()
    {
        using var fixture = await UndoFixture.CreateAsync();
        await fixture.PrepareRestoreAndRecycleAsync(new TestRecycler());
        var transaction = await fixture.TransactionRepository.GetAsync(fixture.Operation.Id);
        Assert.True(await fixture.OperationRepository.UpdateStateAsync(
            fixture.Operation.Id,
            ReplacementOperationStatus.InProgress,
            ReplacementOperationStage.Finalizing,
            fixture.Operation.DestinationSize,
            ReplacementVerificationStatus.Verified,
            null,
            DateTimeOffset.UtcNow));

        Assert.False(await fixture.TransactionRepository.TryCompleteUndoAsync(
            fixture.Operation.Id, transaction!.Revision, DateTimeOffset.UtcNow));

        Assert.Equal(FinalizationCheckpoint.FinalRecycled, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.False(Assert.Single(await fixture.CompletedRepository.GetAllAsync()).SourceExists);
    }

    private sealed class TestRecycler : IRecoverableFileRecycler
    {
        public int CallCount { get; private set; }

        public Task RecycleAsync(string path, CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(path);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingRecycleFaultInjector(FinalFileRecycleFaultPoint target) : IFinalFileRecycleFaultInjector
    {
        public Task OnFaultPointAsync(FinalFileRecycleFaultPoint faultPoint) =>
            faultPoint == target
                ? Task.FromException(new IOException($"Injected interruption at {faultPoint}."))
                : Task.CompletedTask;
    }

    private sealed class UndoFixture : IDisposable
    {
        private UndoFixture(
            string directory,
            ReplacementOperation operation,
            byte[] sourceBytes,
            byte[] finalBytes,
            CompletedEncodeRepository completedRepository,
            ReplacementOperationRepository operationRepository,
            OriginalBackupRepository backupRepository,
            FinalizationTransactionRepository transactionRepository)
        {
            Directory = directory;
            Operation = operation;
            SourceBytes = sourceBytes;
            FinalBytes = finalBytes;
            CompletedRepository = completedRepository;
            OperationRepository = operationRepository;
            BackupRepository = backupRepository;
            TransactionRepository = transactionRepository;
        }

        public string Directory { get; }
        public ReplacementOperation Operation { get; }
        public byte[] SourceBytes { get; }
        public byte[] FinalBytes { get; }
        public CompletedEncodeRepository CompletedRepository { get; }
        public ReplacementOperationRepository OperationRepository { get; }
        public OriginalBackupRepository BackupRepository { get; }
        public FinalizationTransactionRepository TransactionRepository { get; }

        public UndoPreparationService CreatePreparationService() =>
            new(OperationRepository, TransactionRepository);

        public SourceRestorationService CreateRestorationService() =>
            new(OperationRepository, TransactionRepository);

        public FinalFileRecycleService CreateFinalRecycleService(
            IRecoverableFileRecycler recycler,
            IFinalFileRecycleFaultInjector? faultInjector = null) =>
            new(OperationRepository, TransactionRepository, recycler, faultInjector);

        public UndoCompletionService CreateUndoCompletionService() =>
            new(OperationRepository, TransactionRepository);

        public ReplacementRecoveryService CreateRecoveryService() =>
            new(OperationRepository, BackupRepository, TransactionRepository);

        public async Task PrepareAndRestoreAsync()
        {
            await CreatePreparationService().PrepareAsync(Operation.Id);
            await CreateRestorationService().RestoreAsync(Operation.Id);
        }

        public async Task PrepareRestoreAndRecycleAsync(IRecoverableFileRecycler recycler)
        {
            await PrepareAndRestoreAsync();
            await CreateFinalRecycleService(recycler).RecycleAsync(Operation.Id);
        }

        public static async Task<UndoFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"hbcm-undo-{Guid.NewGuid():N}");
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
            var sourceBytes = Enumerable.Range(0, 11_264).Select(value => (byte)(value % 251)).ToArray();
            var finalBytes = Enumerable.Range(0, 5_632).Select(value => (byte)(value % 239)).ToArray();
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
                Guid.NewGuid(), $"UNDO-{Guid.NewGuid():N}", now,
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
            var readyOperation = operation with
            {
                Status = ReplacementOperationStatus.Completed,
                Stage = ReplacementOperationStage.Completed
            };
            var preparedOperation = operation with { Stage = ReplacementOperationStage.BackingUpSource };
            await new FinalizationPreparationService(transactionRepository).PrepareAsync(
                preparedOperation,
                new FinalizationReadinessResult(true, finalHash, sourceHash, []));
            Assert.True(await transactionRepository.TryTransitionAsync(
                operation.Id, FinalizationCheckpoint.Prepared, 0,
                FinalizationCheckpoint.PromoteTemporaryIntentRecorded, null, now.AddSeconds(2)));
            Assert.True(await transactionRepository.TryTransitionAsync(
                operation.Id, FinalizationCheckpoint.PromoteTemporaryIntentRecorded, 1,
                FinalizationCheckpoint.FinalPromoted, null, now.AddSeconds(3)));
            Assert.True(await transactionRepository.TryTransitionAsync(
                operation.Id, FinalizationCheckpoint.FinalPromoted, 2,
                FinalizationCheckpoint.RecycleSourceIntentRecorded, null, now.AddSeconds(4)));
            Assert.True(await transactionRepository.TryTransitionAsync(
                operation.Id, FinalizationCheckpoint.RecycleSourceIntentRecorded, 3,
                FinalizationCheckpoint.SourceRecycled, null, now.AddSeconds(5)));
            Assert.True(await transactionRepository.TryCompleteForwardAsync(
                operation.Id, 4, now.AddSeconds(6)));

            return new UndoFixture(
                directory, readyOperation, sourceBytes, finalBytes,
                completedRepository, operationRepository, backupRepository, transactionRepository);
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
