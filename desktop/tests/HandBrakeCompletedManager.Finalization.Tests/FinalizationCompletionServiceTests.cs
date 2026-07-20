using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Finalization.Tests;

public sealed class FinalizationCompletionServiceTests
{
    [Fact]
    public async Task CompleteAsync_AtomicallyCompletesJournalOperationAndHistory()
    {
        using var fixture = await CompletionFixture.CreateAsync();

        var result = await fixture.CreateService().CompleteAsync(fixture.Operation.Id);
        var transaction = await fixture.TransactionRepository.GetAsync(fixture.Operation.Id);
        var operation = await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id);
        var history = Assert.Single(await fixture.CompletedRepository.GetAllAsync());

        Assert.False(result.WasAlreadyCompleted);
        Assert.Equal(FinalizationCheckpoint.Completed, transaction?.Checkpoint);
        Assert.Equal(ReplacementOperationStatus.Completed, operation?.Status);
        Assert.Equal(ReplacementOperationStage.Completed, operation?.Stage);
        Assert.False(history.SourceExists);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.BackupPath));
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
        Assert.False(File.Exists(fixture.Operation.SourcePath));
        Assert.False(File.Exists(fixture.Operation.TemporaryPath));
    }

    [Fact]
    public async Task CompleteAsync_IsIdempotentAfterAtomicCompletion()
    {
        using var fixture = await CompletionFixture.CreateAsync();
        await fixture.CreateService().CompleteAsync(fixture.Operation.Id);

        var result = await fixture.CreateService().CompleteAsync(fixture.Operation.Id);

        Assert.True(result.WasAlreadyCompleted);
        Assert.Equal(FinalizationCheckpoint.Completed, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.Equal(ReplacementOperationStatus.Completed, (await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id))?.Status);
    }

    [Fact]
    public async Task CompleteAsync_RefusesTamperedBackupWithoutChangingDatabaseState()
    {
        using var fixture = await CompletionFixture.CreateAsync();
        await File.WriteAllBytesAsync(
            fixture.Operation.BackupPath,
            Enumerable.Repeat((byte)61, fixture.SourceBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService().CompleteAsync(fixture.Operation.Id));

        await AssertIncompleteAsync(fixture);
    }

    [Fact]
    public async Task CompleteAsync_RefusesTamperedFinalWithoutChangingDatabaseState()
    {
        using var fixture = await CompletionFixture.CreateAsync();
        await File.WriteAllBytesAsync(
            fixture.Operation.FinalPath,
            Enumerable.Repeat((byte)62, fixture.FinalBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService().CompleteAsync(fixture.Operation.Id));

        await AssertIncompleteAsync(fixture);
    }

    [Fact]
    public async Task CompleteAsync_RefusesReappearedSource()
    {
        using var fixture = await CompletionFixture.CreateAsync();
        await File.WriteAllBytesAsync(fixture.Operation.SourcePath, fixture.SourceBytes);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService().CompleteAsync(fixture.Operation.Id));

        await AssertIncompleteAsync(fixture);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.SourcePath));
    }

    [Fact]
    public async Task CompleteAsync_RefusesUnexpectedTemporaryArtifact()
    {
        using var fixture = await CompletionFixture.CreateAsync();
        await File.WriteAllBytesAsync(fixture.Operation.TemporaryPath, fixture.FinalBytes);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService().CompleteAsync(fixture.Operation.Id));

        await AssertIncompleteAsync(fixture);
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.TemporaryPath));
    }

    [Fact]
    public async Task CompleteAsync_RecordsFailureWhenInterruptedBeforeDatabaseCommit()
    {
        using var fixture = await CompletionFixture.CreateAsync();
        var fault = new ThrowingFaultInjector(FinalizationCompletionFaultPoint.AfterArtifactsVerified);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService(fault).CompleteAsync(fixture.Operation.Id));

        var transaction = await fixture.TransactionRepository.GetAsync(fixture.Operation.Id);
        Assert.Equal(FinalizationCheckpoint.SourceRecycled, transaction?.Checkpoint);
        Assert.Contains("Injected interruption", transaction?.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ReplacementOperationStatus.InProgress, (await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id))?.Status);
        Assert.True(Assert.Single(await fixture.CompletedRepository.GetAllAsync()).SourceExists);
    }

    [Fact]
    public async Task CompleteAsync_RetryRecognizesCommitAfterCallerInterruption()
    {
        using var fixture = await CompletionFixture.CreateAsync();
        var fault = new ThrowingFaultInjector(FinalizationCompletionFaultPoint.AfterDatabaseCommit);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService(fault).CompleteAsync(fixture.Operation.Id));
        Assert.Equal(FinalizationCheckpoint.Completed, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);

        var result = await fixture.CreateService().CompleteAsync(fixture.Operation.Id);
        Assert.True(result.WasAlreadyCompleted);
        Assert.Equal(ReplacementOperationStatus.Completed, (await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id))?.Status);
        Assert.False(Assert.Single(await fixture.CompletedRepository.GetAllAsync()).SourceExists);
    }

    [Fact]
    public async Task TryCompleteForwardAsync_RollsBackJournalWhenOperationBoundaryChanged()
    {
        using var fixture = await CompletionFixture.CreateAsync();
        Assert.True(await fixture.OperationRepository.UpdateStateAsync(
            fixture.Operation.Id,
            ReplacementOperationStatus.InProgress,
            ReplacementOperationStage.Finalizing,
            fixture.Operation.DestinationSize,
            ReplacementVerificationStatus.Verified,
            null,
            DateTimeOffset.UtcNow));

        Assert.False(await fixture.TransactionRepository.TryCompleteForwardAsync(
            fixture.Operation.Id, 4, DateTimeOffset.UtcNow));

        Assert.Equal(FinalizationCheckpoint.SourceRecycled, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.Equal(ReplacementOperationStage.Finalizing, (await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id))?.Stage);
        Assert.True(Assert.Single(await fixture.CompletedRepository.GetAllAsync()).SourceExists);
    }

    [Fact]
    public async Task CompleteAsync_RemovesCompletedTransactionFromRestartRecoveryList()
    {
        using var fixture = await CompletionFixture.CreateAsync();
        var recovery = new ReplacementRecoveryService(
            fixture.OperationRepository,
            fixture.BackupRepository,
            fixture.TransactionRepository);
        var before = Assert.Single(await recovery.ReviewAsync());
        Assert.Equal(ReplacementRecoveryAction.ReviewFinalizationTransaction, before.Action);
        Assert.Contains("SourceRecycled", before.Summary, StringComparison.Ordinal);

        await fixture.CreateService().CompleteAsync(fixture.Operation.Id);

        Assert.Empty(await recovery.ReviewAsync());
    }

    private static async Task AssertIncompleteAsync(CompletionFixture fixture)
    {
        Assert.Equal(FinalizationCheckpoint.SourceRecycled, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.Equal(ReplacementOperationStatus.InProgress, (await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id))?.Status);
        Assert.True(Assert.Single(await fixture.CompletedRepository.GetAllAsync()).SourceExists);
    }

    private sealed class ThrowingFaultInjector(FinalizationCompletionFaultPoint target) : IFinalizationCompletionFaultInjector
    {
        public Task OnFaultPointAsync(FinalizationCompletionFaultPoint faultPoint) =>
            faultPoint == target
                ? Task.FromException(new IOException($"Injected interruption at {faultPoint}."))
                : Task.CompletedTask;
    }

    private sealed class CompletionFixture : IDisposable
    {
        private CompletionFixture(
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

        public FinalizationCompletionService CreateService(IFinalizationCompletionFaultInjector? faultInjector = null) =>
            new(OperationRepository, TransactionRepository, faultInjector);

        public static async Task<CompletionFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"hbcm-final-completion-{Guid.NewGuid():N}");
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
            var sourceBytes = Enumerable.Range(0, 9_216).Select(value => (byte)(value % 251)).ToArray();
            var finalBytes = Enumerable.Range(0, 4_608).Select(value => (byte)(value % 239)).ToArray();
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
                Guid.NewGuid(), $"COMPLETE-{Guid.NewGuid():N}", now,
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
            Assert.True(await transactionRepository.TryTransitionAsync(
                operation.Id, FinalizationCheckpoint.FinalPromoted, 2,
                FinalizationCheckpoint.RecycleSourceIntentRecorded, null, now.AddSeconds(4)));
            Assert.True(await transactionRepository.TryTransitionAsync(
                operation.Id, FinalizationCheckpoint.RecycleSourceIntentRecorded, 3,
                FinalizationCheckpoint.SourceRecycled, null, now.AddSeconds(5)));

            return new CompletionFixture(
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
