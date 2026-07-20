using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Finalization.Tests;

public sealed class FinalizationPromotionServiceTests
{
    [Fact]
    public async Task PromoteAsync_AtomicallyPromotesVerifiedCopyAndPreservesSourceAndBackup()
    {
        using var fixture = await PromotionFixture.CreateAsync();

        var result = await fixture.CreateService().PromoteAsync(fixture.Operation.Id);
        var transaction = await fixture.TransactionRepository.GetAsync(fixture.Operation.Id);

        Assert.False(result.WasRecovered);
        Assert.False(File.Exists(fixture.Operation.TemporaryPath));
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.SourcePath));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.BackupPath));
        Assert.Equal(FinalizationCheckpoint.FinalPromoted, transaction?.Checkpoint);
        Assert.Null(transaction?.FailureMessage);
    }

    [Fact]
    public async Task PromoteAsync_RefusesOccupiedFinalPathWithoutRecordingIntent()
    {
        using var fixture = await PromotionFixture.CreateAsync();
        var existing = new byte[] { 7, 8, 9 };
        await File.WriteAllBytesAsync(fixture.Operation.FinalPath, existing);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService().PromoteAsync(fixture.Operation.Id));

        Assert.Equal(existing, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
        Assert.True(File.Exists(fixture.Operation.TemporaryPath));
        Assert.Equal(FinalizationCheckpoint.Prepared, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task PromoteAsync_RefusesDirectoryAtFinalPath()
    {
        using var fixture = await PromotionFixture.CreateAsync();
        System.IO.Directory.CreateDirectory(fixture.Operation.FinalPath);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService().PromoteAsync(fixture.Operation.Id));

        Assert.True(Directory.Exists(fixture.Operation.FinalPath));
        Assert.True(File.Exists(fixture.Operation.TemporaryPath));
    }

    [Fact]
    public async Task PromoteAsync_RefusesLockedTemporaryBeforeRecordingIntent()
    {
        using var fixture = await PromotionFixture.CreateAsync();
        using var lockStream = new FileStream(
            fixture.Operation.TemporaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService().PromoteAsync(fixture.Operation.Id));

        Assert.Equal(FinalizationCheckpoint.Prepared, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task PromoteAsync_RefusesLockedSourceBeforeRecordingIntent()
    {
        using var fixture = await PromotionFixture.CreateAsync();
        using var lockStream = new FileStream(
            fixture.Operation.SourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService().PromoteAsync(fixture.Operation.Id));

        Assert.Equal(FinalizationCheckpoint.Prepared, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task PromoteAsync_RefusesSameSizeTamperingBeforeRecordingIntent()
    {
        using var fixture = await PromotionFixture.CreateAsync();
        await File.WriteAllBytesAsync(
            fixture.Operation.TemporaryPath,
            Enumerable.Repeat((byte)77, fixture.FinalBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService().PromoteAsync(fixture.Operation.Id));

        Assert.Equal(FinalizationCheckpoint.Prepared, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.False(File.Exists(fixture.Operation.FinalPath));
    }

    [Fact]
    public async Task PromoteAsync_RetriesWhenCrashOccursAfterIntentBeforeMove()
    {
        using var fixture = await PromotionFixture.CreateAsync();
        var fault = new ThrowingFaultInjector(FinalizationPromotionFaultPoint.AfterIntentPersisted);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService(fault).PromoteAsync(fixture.Operation.Id));
        var interrupted = await fixture.TransactionRepository.GetAsync(fixture.Operation.Id);
        Assert.Equal(FinalizationCheckpoint.PromoteTemporaryIntentRecorded, interrupted?.Checkpoint);
        Assert.NotNull(interrupted?.FailureMessage);
        Assert.True(File.Exists(fixture.Operation.TemporaryPath));
        Assert.False(File.Exists(fixture.Operation.FinalPath));

        var result = await fixture.CreateService().PromoteAsync(fixture.Operation.Id);
        Assert.False(result.WasRecovered);
        Assert.Equal(FinalizationCheckpoint.FinalPromoted, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task PromoteAsync_RecordsCompletedMoveWhenCrashOccursBeforeCompletionCheckpoint()
    {
        using var fixture = await PromotionFixture.CreateAsync();
        var fault = new ThrowingFaultInjector(FinalizationPromotionFaultPoint.AfterAtomicMove);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService(fault).PromoteAsync(fixture.Operation.Id));
        Assert.False(File.Exists(fixture.Operation.TemporaryPath));
        Assert.True(File.Exists(fixture.Operation.FinalPath));
        Assert.Equal(
            FinalizationCheckpoint.PromoteTemporaryIntentRecorded,
            (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);

        var result = await fixture.CreateService().PromoteAsync(fixture.Operation.Id);
        Assert.True(result.WasRecovered);
        Assert.Equal(FinalizationCheckpoint.FinalPromoted, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.SourcePath));
    }

    [Fact]
    public async Task PromoteAsync_RefusesTamperedFinalDuringRecoveryAndRecordsFailure()
    {
        using var fixture = await PromotionFixture.CreateAsync();
        var fault = new ThrowingFaultInjector(FinalizationPromotionFaultPoint.AfterAtomicMove);
        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService(fault).PromoteAsync(fixture.Operation.Id));
        await File.WriteAllBytesAsync(
            fixture.Operation.FinalPath,
            Enumerable.Repeat((byte)31, fixture.FinalBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService().PromoteAsync(fixture.Operation.Id));

        var transaction = await fixture.TransactionRepository.GetAsync(fixture.Operation.Id);
        Assert.Equal(FinalizationCheckpoint.PromoteTemporaryIntentRecorded, transaction?.Checkpoint);
        Assert.Contains("digest changed", transaction?.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.SourcePath));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.BackupPath));
    }

    [Fact]
    public async Task PromoteAsync_RefusesCrossDirectoryPaths()
    {
        using var fixture = await PromotionFixture.CreateAsync(crossDirectoryTemporary: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService().PromoteAsync(fixture.Operation.Id));

        Assert.True(File.Exists(fixture.Operation.TemporaryPath));
        Assert.Equal(FinalizationCheckpoint.Prepared, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
    }

    [Fact]
    public async Task PromoteAsync_RequiresPreparedTransaction()
    {
        using var fixture = await PromotionFixture.CreateAsync(prepareTransaction: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService().PromoteAsync(fixture.Operation.Id));

        Assert.True(File.Exists(fixture.Operation.TemporaryPath));
        Assert.False(File.Exists(fixture.Operation.FinalPath));
    }

    private sealed class ThrowingFaultInjector(FinalizationPromotionFaultPoint target) : IFinalizationPromotionFaultInjector
    {
        public Task OnFaultPointAsync(FinalizationPromotionFaultPoint faultPoint) =>
            faultPoint == target
                ? Task.FromException(new IOException($"Injected interruption at {faultPoint}."))
                : Task.CompletedTask;
    }

    private sealed class PromotionFixture : IDisposable
    {
        private PromotionFixture(
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

        public FinalizationPromotionService CreateService(IFinalizationPromotionFaultInjector? faultInjector = null) =>
            new(OperationRepository, TransactionRepository, faultInjector);

        public static async Task<PromotionFixture> CreateAsync(
            bool crossDirectoryTemporary = false,
            bool prepareTransaction = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"hbcm-promotion-{Guid.NewGuid():N}");
            var sourceDirectory = Path.Combine(directory, "source");
            var convertedDirectory = Path.Combine(directory, "converted");
            var temporaryDirectory = crossDirectoryTemporary
                ? Path.Combine(directory, "other-volume-simulation")
                : sourceDirectory;
            System.IO.Directory.CreateDirectory(sourceDirectory);
            System.IO.Directory.CreateDirectory(convertedDirectory);
            System.IO.Directory.CreateDirectory(temporaryDirectory);
            var sourcePath = Path.Combine(sourceDirectory, "video.mkv");
            var destinationPath = Path.Combine(convertedDirectory, "video.mp4");
            var finalPath = Path.Combine(sourceDirectory, "video.mp4");
            var temporaryPath = Path.Combine(temporaryDirectory, "video.mp4.hbcm-copying");
            var backupPath = Path.Combine(sourceDirectory, "HandBrake Original Backup", "video.mkv");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            var sourceBytes = Enumerable.Range(0, 8_192).Select(value => (byte)(value % 251)).ToArray();
            var finalBytes = Enumerable.Range(0, 4_096).Select(value => (byte)(value % 239)).ToArray();
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            await File.WriteAllBytesAsync(destinationPath, finalBytes);
            await File.WriteAllBytesAsync(temporaryPath, finalBytes);
            await File.WriteAllBytesAsync(backupPath, sourceBytes);

            var databasePath = Path.Combine(directory, "history.db");
            var completedRepository = new CompletedEncodeRepository(databasePath);
            var operationRepository = new ReplacementOperationRepository(databasePath);
            var backupRepository = new OriginalBackupRepository(databasePath);
            var transactionRepository = new FinalizationTransactionRepository(databasePath);
            var now = DateTimeOffset.UtcNow;
            var encode = new CompletedEncode(
                Guid.NewGuid(), $"PROMOTION-{Guid.NewGuid():N}", now,
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
            if (prepareTransaction)
            {
                var preparation = new FinalizationPreparationService(transactionRepository);
                await preparation.PrepareAsync(
                    readyOperation,
                    new FinalizationReadinessResult(true, finalHash, sourceHash, []));
            }

            return new PromotionFixture(
                directory,
                readyOperation,
                sourceBytes,
                finalBytes,
                operationRepository,
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
