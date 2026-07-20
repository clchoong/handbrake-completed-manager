using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Finalization.Tests;

public sealed class FinalizationJournalTests
{
    [Fact]
    public async Task PrepareAsync_PersistsPreparedTransactionFromVerifiedState()
    {
        using var fixture = await JournalFixture.CreateAsync();
        var transaction = await fixture.Service.PrepareAsync(fixture.Operation, fixture.Readiness);
        Assert.Equal(transaction, await fixture.Repository.GetAsync(fixture.Operation.Id));
        Assert.Equal(FinalizationCheckpoint.Prepared, transaction.Checkpoint);
        Assert.Equal(0, transaction.Revision);
    }

    [Fact]
    public async Task PrepareAsync_IsIdempotentForMatchingDigests()
    {
        using var fixture = await JournalFixture.CreateAsync();
        var first = await fixture.Service.PrepareAsync(fixture.Operation, fixture.Readiness);
        Assert.Equal(first, await fixture.Service.PrepareAsync(fixture.Operation, fixture.Readiness));
    }

    [Fact]
    public async Task PrepareAsync_RejectsDifferentDigestForExistingTransaction()
    {
        using var fixture = await JournalFixture.CreateAsync();
        await fixture.Service.PrepareAsync(fixture.Operation, fixture.Readiness);
        var changed = fixture.Readiness with { TemporarySha256 = new string('C', 64) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.PrepareAsync(fixture.Operation, changed));
    }

    [Fact]
    public async Task TryCreatePreparedAsync_RejectsUnverifiedPersistedOperation()
    {
        using var fixture = await JournalFixture.CreateAsync(OriginalBackupStatus.Failed);
        var now = DateTimeOffset.UtcNow;
        var transaction = new FinalizationTransaction(
            fixture.Operation.Id, FinalizationCheckpoint.Prepared, SourceHash, FinalHash, 0, null, now, now);
        Assert.False(await fixture.Repository.TryCreatePreparedAsync(transaction));
        Assert.Null(await fixture.Repository.GetAsync(fixture.Operation.Id));
    }

    [Fact]
    public async Task TryTransitionAsync_UsesCheckpointAndRevisionConcurrencyGuard()
    {
        using var fixture = await JournalFixture.CreateAsync();
        var prepared = await fixture.Service.PrepareAsync(fixture.Operation, fixture.Readiness);
        Assert.True(await fixture.Repository.TryTransitionAsync(
            prepared.OperationId, prepared.Checkpoint, prepared.Revision,
            FinalizationCheckpoint.PromoteTemporaryIntentRecorded, null, prepared.DateUpdatedUtc.AddMinutes(1)));
        Assert.False(await fixture.Repository.TryTransitionAsync(
            prepared.OperationId, prepared.Checkpoint, prepared.Revision,
            FinalizationCheckpoint.PromoteTemporaryIntentRecorded, null, prepared.DateUpdatedUtc.AddMinutes(2)));
        var persisted = await fixture.Repository.GetAsync(prepared.OperationId);
        Assert.Equal(FinalizationCheckpoint.PromoteTemporaryIntentRecorded, persisted?.Checkpoint);
        Assert.Equal(1, persisted?.Revision);
    }

    [Fact]
    public async Task TryTransitionAsync_RejectsSkippedCheckpoint()
    {
        using var fixture = await JournalFixture.CreateAsync();
        var prepared = await fixture.Service.PrepareAsync(fixture.Operation, fixture.Readiness);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.TryTransitionAsync(
            prepared.OperationId, prepared.Checkpoint, prepared.Revision,
            FinalizationCheckpoint.Completed, null, DateTimeOffset.UtcNow));
    }

    private sealed class JournalFixture : IDisposable
    {
        private JournalFixture(string directory, ReplacementOperation operation,
            FinalizationReadinessResult readiness, FinalizationTransactionRepository repository)
        {
            Directory = directory;
            Operation = operation;
            Readiness = readiness;
            Repository = repository;
            Service = new FinalizationPreparationService(repository);
        }

        public string Directory { get; }
        public ReplacementOperation Operation { get; }
        public FinalizationReadinessResult Readiness { get; }
        public FinalizationTransactionRepository Repository { get; }
        public FinalizationPreparationService Service { get; }

        public static async Task<JournalFixture> CreateAsync(OriginalBackupStatus backupStatus = OriginalBackupStatus.Verified)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"hbcm-journal-{Guid.NewGuid():N}");
            var databasePath = Path.Combine(directory, "history.db");
            var completedRepository = new CompletedEncodeRepository(databasePath);
            var operationRepository = new ReplacementOperationRepository(databasePath);
            var backupRepository = new OriginalBackupRepository(databasePath);
            var repository = new FinalizationTransactionRepository(databasePath);
            var now = new DateTimeOffset(2026, 7, 20, 6, 0, 0, TimeSpan.Zero);
            var encode = new CompletedEncode(
                Guid.NewGuid(), $"JOURNAL-{Guid.NewGuid():N}", now,
                @"C:\Videos\Source.mkv", "Source.mkv", ".mkv", 2_000, true,
                @"D:\Converted\Output.mp4", "Output.mp4", ".mp4", 800, true,
                now, 40, 60, 1_200, 0, "Completed", now, now);
            var operation = new ReplacementOperation(
                Guid.NewGuid(), encode.Id, ReplacementOperationStatus.InProgress, ReplacementOperationStage.Verifying,
                encode.SourcePath, encode.DestinationPath,
                @"C:\Videos\Source.mp4", @"C:\Videos\Source.mp4.hbcm-copying",
                @"C:\Videos\HandBrake Original Backup\Source.mkv",
                2_000, 800, 800, ReplacementVerificationStatus.Verified, null, now, now);
            var backup = new OriginalBackupState(
                operation.Id, operation.BackupPath, backupStatus, 2_000, 2_000,
                backupStatus == OriginalBackupStatus.Verified ? SourceHash : null,
                backupStatus == OriginalBackupStatus.Failed ? "Injected failure." : null, now, now);
            await completedRepository.InitializeAsync();
            Assert.True(await completedRepository.AddAsync(encode));
            await operationRepository.AddAsync(operation);
            Assert.True(await backupRepository.TryBeginAsync(backup, now));
            Assert.True(await backupRepository.UpdateAsync(
                operation.Id, backupStatus, backup.BytesCopied, backup.Sha256, backup.FailureMessage, now.AddSeconds(1)));
            return new JournalFixture(
                directory,
                operation with { Stage = ReplacementOperationStage.BackingUpSource },
                new FinalizationReadinessResult(true, FinalHash, SourceHash, []),
                repository);
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

    private static readonly string SourceHash = new('A', 64);
    private static readonly string FinalHash = new('B', 64);
}
