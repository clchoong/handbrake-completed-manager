using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Finalization.Tests;

public sealed class SourceRestorationServiceTests
{
    [Fact]
    public async Task RestoreAsync_RestoresVerifiedSourceAndLeavesFinalAndBackupUntouched()
    {
        using var fixture = await RestorationFixture.CreateAsync();

        var result = await fixture.CreateService().RestoreAsync(fixture.Operation.Id);
        var transaction = await fixture.TransactionRepository.GetAsync(fixture.Operation.Id);

        Assert.False(result.WasRecovered);
        Assert.False(result.WasResumed);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.SourcePath));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.BackupPath));
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
        Assert.False(File.Exists(fixture.RestorePath));
        Assert.Equal(FinalizationCheckpoint.SourceRestored, transaction?.Checkpoint);
        Assert.Null(transaction?.FailureMessage);
    }

    [Fact]
    public async Task RestoreAsync_RefusesOccupiedSourceWithoutRecordingIntent()
    {
        using var fixture = await RestorationFixture.CreateAsync();
        var unrelated = new byte[] { 9, 8, 7 };
        await File.WriteAllBytesAsync(fixture.Operation.SourcePath, unrelated);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService().RestoreAsync(fixture.Operation.Id));

        Assert.Equal(unrelated, await File.ReadAllBytesAsync(fixture.Operation.SourcePath));
        Assert.Equal(FinalizationCheckpoint.UndoPrepared, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.False(File.Exists(fixture.RestorePath));
    }

    [Fact]
    public async Task RestoreAsync_RefusesTamperedBackupBeforeRecordingIntent()
    {
        using var fixture = await RestorationFixture.CreateAsync();
        await File.WriteAllBytesAsync(
            fixture.Operation.BackupPath,
            Enumerable.Repeat((byte)41, fixture.SourceBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService().RestoreAsync(fixture.Operation.Id));

        Assert.False(File.Exists(fixture.Operation.SourcePath));
        Assert.Equal(FinalizationCheckpoint.UndoPrepared, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
    }

    [Fact]
    public async Task RestoreAsync_RefusesChangedFinalBeforeRecordingIntent()
    {
        using var fixture = await RestorationFixture.CreateAsync();
        await File.WriteAllBytesAsync(
            fixture.Operation.FinalPath,
            Enumerable.Repeat((byte)73, fixture.FinalBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService().RestoreAsync(fixture.Operation.Id));

        Assert.False(File.Exists(fixture.Operation.SourcePath));
        Assert.Equal(FinalizationCheckpoint.UndoPrepared, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.False(File.Exists(fixture.RestorePath));
    }

    [Fact]
    public async Task RestoreAsync_ResumesMatchingPartialRestore()
    {
        using var fixture = await RestorationFixture.CreateAsync();
        await File.WriteAllBytesAsync(fixture.RestorePath, fixture.SourceBytes[..1_337]);

        var result = await fixture.CreateService().RestoreAsync(fixture.Operation.Id);

        Assert.True(result.WasResumed);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.SourcePath));
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
        Assert.False(File.Exists(fixture.RestorePath));
    }

    [Fact]
    public async Task RestoreAsync_RefusesMismatchedPartialRestoreAndKeepsItForReview()
    {
        using var fixture = await RestorationFixture.CreateAsync();
        var partial = Enumerable.Repeat((byte)201, 512).ToArray();
        await File.WriteAllBytesAsync(fixture.RestorePath, partial);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService().RestoreAsync(fixture.Operation.Id));

        var transaction = await fixture.TransactionRepository.GetAsync(fixture.Operation.Id);
        Assert.Equal(partial, await File.ReadAllBytesAsync(fixture.RestorePath));
        Assert.False(File.Exists(fixture.Operation.SourcePath));
        Assert.Equal(FinalizationCheckpoint.RestoreSourceIntentRecorded, transaction?.Checkpoint);
        Assert.Contains("does not match", transaction?.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
    }

    [Fact]
    public async Task RestoreAsync_RetriesAfterIntentWasPersisted()
    {
        using var fixture = await RestorationFixture.CreateAsync();
        var fault = new ThrowingFaultInjector(SourceRestorationFaultPoint.AfterIntentPersisted);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService(fault).RestoreAsync(fixture.Operation.Id));
        Assert.Equal(
            FinalizationCheckpoint.RestoreSourceIntentRecorded,
            (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.False(File.Exists(fixture.Operation.SourcePath));

        var result = await fixture.CreateService().RestoreAsync(fixture.Operation.Id);
        Assert.False(result.WasRecovered);
        Assert.False(result.WasResumed);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.SourcePath));
    }

    [Fact]
    public async Task RestoreAsync_ResumesVerifiedRestoreFileAfterInterruption()
    {
        using var fixture = await RestorationFixture.CreateAsync();
        var fault = new ThrowingFaultInjector(SourceRestorationFaultPoint.AfterRestoreFileVerified);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService(fault).RestoreAsync(fixture.Operation.Id));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.RestorePath));
        Assert.False(File.Exists(fixture.Operation.SourcePath));

        var result = await fixture.CreateService().RestoreAsync(fixture.Operation.Id);
        Assert.True(result.WasResumed);
        Assert.False(result.WasRecovered);
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.SourcePath));
    }

    [Fact]
    public async Task RestoreAsync_RecordsCompletedMoveAfterInterruption()
    {
        using var fixture = await RestorationFixture.CreateAsync();
        var fault = new ThrowingFaultInjector(SourceRestorationFaultPoint.AfterAtomicMove);

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService(fault).RestoreAsync(fixture.Operation.Id));
        Assert.True(File.Exists(fixture.Operation.SourcePath));
        Assert.False(File.Exists(fixture.RestorePath));
        Assert.Equal(
            FinalizationCheckpoint.RestoreSourceIntentRecorded,
            (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);

        var result = await fixture.CreateService().RestoreAsync(fixture.Operation.Id);
        Assert.True(result.WasRecovered);
        Assert.Equal(FinalizationCheckpoint.SourceRestored, (await fixture.TransactionRepository.GetAsync(fixture.Operation.Id))?.Checkpoint);
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
    }

    [Fact]
    public async Task RestoreAsync_RefusesTamperedRestoredSourceDuringRecovery()
    {
        using var fixture = await RestorationFixture.CreateAsync();
        var fault = new ThrowingFaultInjector(SourceRestorationFaultPoint.AfterAtomicMove);
        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService(fault).RestoreAsync(fixture.Operation.Id));
        await File.WriteAllBytesAsync(
            fixture.Operation.SourcePath,
            Enumerable.Repeat((byte)88, fixture.SourceBytes.Length).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService().RestoreAsync(fixture.Operation.Id));

        var transaction = await fixture.TransactionRepository.GetAsync(fixture.Operation.Id);
        Assert.Equal(FinalizationCheckpoint.RestoreSourceIntentRecorded, transaction?.Checkpoint);
        Assert.Contains("digest", transaction?.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fixture.FinalBytes, await File.ReadAllBytesAsync(fixture.Operation.FinalPath));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.Operation.BackupPath));
    }

    private sealed class ThrowingFaultInjector(SourceRestorationFaultPoint target) : ISourceRestorationFaultInjector
    {
        public Task OnFaultPointAsync(SourceRestorationFaultPoint faultPoint) =>
            faultPoint == target
                ? Task.FromException(new IOException($"Injected interruption at {faultPoint}."))
                : Task.CompletedTask;
    }

    private sealed class RestorationFixture : IDisposable
    {
        private RestorationFixture(
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
            RestorePath = SourceRestorationService.GetRestoreTemporaryPath(operation);
        }

        public string Directory { get; }
        public ReplacementOperation Operation { get; }
        public byte[] SourceBytes { get; }
        public byte[] FinalBytes { get; }
        public ReplacementOperationRepository OperationRepository { get; }
        public FinalizationTransactionRepository TransactionRepository { get; }
        public string RestorePath { get; }

        public SourceRestorationService CreateService(ISourceRestorationFaultInjector? faultInjector = null) =>
            new(OperationRepository, TransactionRepository, faultInjector);

        public static async Task<RestorationFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"hbcm-restoration-{Guid.NewGuid():N}");
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
            var sourceBytes = Enumerable.Range(0, 12_288).Select(value => (byte)(value % 251)).ToArray();
            var finalBytes = Enumerable.Range(0, 6_144).Select(value => (byte)(value % 239)).ToArray();
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
                Guid.NewGuid(), $"RESTORE-{Guid.NewGuid():N}", now,
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
                FinalizationCheckpoint.UndoPrepared, null, now.AddSeconds(4)));
            File.Delete(sourcePath);

            return new RestorationFixture(
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
