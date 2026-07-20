using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class OriginalBackupServiceTests
{
    [Fact]
    public async Task CopyAndVerifyAsync_CreatesVerifiedBackupAndPreservesEveryExistingFile()
    {
        var fixture = await BackupFixture.CreateAsync();
        var progressUpdates = new List<OriginalBackupProgress>();

        try
        {
            var result = await fixture.CreateService(long.MaxValue).CopyAndVerifyAsync(
                fixture.Plan,
                fixture.Operation,
                new InlineProgress<OriginalBackupProgress>(progressUpdates.Add));
            var backup = await fixture.BackupRepository.GetAsync(fixture.Operation.Id);
            var operation = await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id);

            Assert.Equal(fixture.SourceBytes.Length, result.BytesCopied);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(fixture.SourceBytes)), result.Sha256);
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(result.BackupPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
            Assert.Equal(fixture.DestinationBytes, await File.ReadAllBytesAsync(fixture.DestinationPath));
            Assert.Equal(fixture.DestinationBytes, await File.ReadAllBytesAsync(fixture.Plan.Paths.TemporaryPath));
            Assert.False(File.Exists(fixture.Plan.Paths.FinalPath));
            Assert.NotNull(backup);
            Assert.Equal(OriginalBackupStatus.Verified, backup.Status);
            Assert.Equal(result.Sha256, backup.Sha256);
            Assert.NotNull(operation);
            Assert.Equal(ReplacementOperationStage.BackingUpSource, operation.Stage);
            Assert.NotEmpty(progressUpdates);
            Assert.Equal(100, progressUpdates[^1].Percentage, precision: 6);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CopyAndVerifyAsync_PersistsInsufficientSpaceFailureWithoutCreatingBackup()
    {
        var fixture = await BackupFixture.CreateAsync();

        try
        {
            await Assert.ThrowsAsync<InsufficientBackupSpaceException>(() =>
                fixture.CreateService(fixture.SourceBytes.Length - 1).CopyAndVerifyAsync(
                    fixture.Plan,
                    fixture.Operation));
            var backup = await fixture.BackupRepository.GetAsync(fixture.Operation.Id);
            var operation = await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id);

            Assert.NotNull(backup);
            Assert.Equal(OriginalBackupStatus.Failed, backup.Status);
            Assert.Equal(0, backup.BytesCopied);
            Assert.False(File.Exists(fixture.Plan.Paths.BackupPath));
            Assert.NotNull(operation);
            Assert.Equal(ReplacementOperationStage.Verifying, operation.Stage);
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CopyAndVerifyAsync_RetainsPartialBackupAndProgressWhenCancelled()
    {
        var fixture = await BackupFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<OriginalBackupProgress>(_ => cancellation.Cancel());

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.CreateService(long.MaxValue).CopyAndVerifyAsync(
                    fixture.Plan,
                    fixture.Operation,
                    progress,
                    cancellation.Token));
            var backup = await fixture.BackupRepository.GetAsync(fixture.Operation.Id);
            var operation = await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id);
            var partialLength = new FileInfo(fixture.Plan.Paths.BackupPath).Length;

            Assert.True(partialLength > 0);
            Assert.True(partialLength < fixture.SourceBytes.Length);
            Assert.NotNull(backup);
            Assert.Equal(OriginalBackupStatus.Cancelled, backup.Status);
            Assert.Equal(partialLength, backup.BytesCopied);
            Assert.NotNull(operation);
            Assert.Equal(ReplacementOperationStage.Verifying, operation.Stage);
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
            Assert.False(File.Exists(fixture.Plan.Paths.FinalPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CopyAndVerifyAsync_RefusesExistingBackupWithoutOverwritingIt()
    {
        var fixture = await BackupFixture.CreateAsync();
        var existingBytes = new byte[] { 1, 3, 5, 7 };
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(fixture.Plan.Paths.BackupPath)!);
        await File.WriteAllBytesAsync(fixture.Plan.Paths.BackupPath, existingBytes);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.CreateService(long.MaxValue).CopyAndVerifyAsync(
                    fixture.Plan,
                    fixture.Operation));

            Assert.Equal(existingBytes, await File.ReadAllBytesAsync(fixture.Plan.Paths.BackupPath));
            Assert.Null(await fixture.BackupRepository.GetAsync(fixture.Operation.Id));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DiscardAsync_RemovesOnlyBackupArtifactAndReturnsOperationToVerifiedTemporaryStage()
    {
        var fixture = await BackupFixture.CreateAsync();

        try
        {
            await fixture.CreateService(long.MaxValue).CopyAndVerifyAsync(
                fixture.Plan,
                fixture.Operation);
            var backup = Assert.IsType<OriginalBackupState>(
                await fixture.BackupRepository.GetAsync(fixture.Operation.Id));
            var operation = Assert.IsType<ReplacementOperation>(
                await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id));
            var cleanup = new OriginalBackupCleanupService(
                fixture.OperationRepository,
                fixture.BackupRepository);

            var result = await cleanup.DiscardAsync(fixture.Plan, operation, backup);
            var updatedBackup = await fixture.BackupRepository.GetAsync(fixture.Operation.Id);
            var updatedOperation = await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id);

            Assert.Equal(fixture.SourceBytes.Length, result.BytesRemoved);
            Assert.False(File.Exists(fixture.Plan.Paths.BackupPath));
            Assert.NotNull(updatedBackup);
            Assert.Equal(OriginalBackupStatus.Cancelled, updatedBackup.Status);
            Assert.NotNull(updatedOperation);
            Assert.Equal(ReplacementOperationStage.Verifying, updatedOperation.Stage);
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
            Assert.Equal(fixture.DestinationBytes, await File.ReadAllBytesAsync(fixture.Plan.Paths.TemporaryPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DiscardAsync_RefusesBackupArtifactLockedByActiveWriter()
    {
        var fixture = await BackupFixture.CreateAsync();

        try
        {
            await fixture.CreateService(long.MaxValue).CopyAndVerifyAsync(
                fixture.Plan,
                fixture.Operation);
            var backup = Assert.IsType<OriginalBackupState>(
                await fixture.BackupRepository.GetAsync(fixture.Operation.Id));
            var operation = Assert.IsType<ReplacementOperation>(
                await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id));
            await using var lockStream = new FileStream(
                backup.BackupPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            var cleanup = new OriginalBackupCleanupService(
                fixture.OperationRepository,
                fixture.BackupRepository);

            await Assert.ThrowsAsync<IOException>(() =>
                cleanup.DiscardAsync(fixture.Plan, operation, backup));

            Assert.True(File.Exists(backup.BackupPath));
            Assert.Equal(OriginalBackupStatus.Verified,
                (await fixture.BackupRepository.GetAsync(fixture.Operation.Id))?.Status);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CopyAndVerifyAsync_RefusesOperationWithoutVerifiedTemporaryCopy()
    {
        var fixture = await BackupFixture.CreateAsync();
        var unverified = fixture.Operation with
        {
            VerificationStatus = ReplacementVerificationStatus.NotVerified
        };

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.CreateService(long.MaxValue).CopyAndVerifyAsync(fixture.Plan, unverified));

            Assert.Null(await fixture.BackupRepository.GetAsync(fixture.Operation.Id));
            Assert.False(File.Exists(fixture.Plan.Paths.BackupPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CopyAndVerifyAsync_RefusesOperationChangedSinceReview()
    {
        var fixture = await BackupFixture.CreateAsync();
        Assert.True(await fixture.OperationRepository.UpdateStateAsync(
            fixture.Operation.Id,
            ReplacementOperationStatus.InProgress,
            ReplacementOperationStage.Verifying,
            fixture.Operation.BytesCopied,
            ReplacementVerificationStatus.Verified,
            null,
            fixture.Operation.DateUpdatedUtc.AddMinutes(1)));

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.CreateService(long.MaxValue).CopyAndVerifyAsync(
                    fixture.Plan,
                    fixture.Operation));

            Assert.Null(await fixture.BackupRepository.GetAsync(fixture.Operation.Id));
            Assert.False(File.Exists(fixture.Plan.Paths.BackupPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CopyAndVerifyAsync_PersistsFailureWhenSourceIsLockedForWriting()
    {
        var fixture = await BackupFixture.CreateAsync();

        try
        {
            await using var sourceLock = new FileStream(
                fixture.SourcePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            await Assert.ThrowsAsync<IOException>(() =>
                fixture.CreateService(long.MaxValue).CopyAndVerifyAsync(
                    fixture.Plan,
                    fixture.Operation));
            var backup = await fixture.BackupRepository.GetAsync(fixture.Operation.Id);

            Assert.NotNull(backup);
            Assert.Equal(OriginalBackupStatus.Failed, backup.Status);
            Assert.False(File.Exists(fixture.Plan.Paths.BackupPath));
            Assert.Equal(ReplacementOperationStage.Verifying,
                (await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id))?.Stage);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DiscardAsync_RefusesBackupStateWithMismatchedPath()
    {
        var fixture = await BackupFixture.CreateAsync();

        try
        {
            await fixture.CreateService(long.MaxValue).CopyAndVerifyAsync(
                fixture.Plan,
                fixture.Operation);
            var backup = Assert.IsType<OriginalBackupState>(
                await fixture.BackupRepository.GetAsync(fixture.Operation.Id));
            var operation = Assert.IsType<ReplacementOperation>(
                await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id));
            var mismatched = backup with { BackupPath = fixture.SourcePath };
            var cleanup = new OriginalBackupCleanupService(
                fixture.OperationRepository,
                fixture.BackupRepository);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cleanup.DiscardAsync(fixture.Plan, operation, mismatched));

            Assert.True(File.Exists(fixture.Plan.Paths.BackupPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private sealed class FixedAvailableSpaceProvider(long availableBytes) : IAvailableSpaceProvider
    {
        public long GetAvailableBytes(string path) => availableBytes;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class BackupFixture : IDisposable
    {
        private BackupFixture(
            string directory,
            string sourcePath,
            string destinationPath,
            byte[] sourceBytes,
            byte[] destinationBytes,
            ReplacementPlan plan,
            ReplacementOperation operation,
            ReplacementOperationRepository operationRepository,
            OriginalBackupRepository backupRepository)
        {
            Directory = directory;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
            SourceBytes = sourceBytes;
            DestinationBytes = destinationBytes;
            Plan = plan;
            Operation = operation;
            OperationRepository = operationRepository;
            BackupRepository = backupRepository;
        }

        public string Directory { get; }
        public string SourcePath { get; }
        public string DestinationPath { get; }
        public byte[] SourceBytes { get; }
        public byte[] DestinationBytes { get; }
        public ReplacementPlan Plan { get; }
        public ReplacementOperation Operation { get; }
        public ReplacementOperationRepository OperationRepository { get; }
        public OriginalBackupRepository BackupRepository { get; }

        public OriginalBackupService CreateService(long availableBytes) => new(
            OperationRepository,
            BackupRepository,
            new FixedAvailableSpaceProvider(availableBytes));

        public static async Task<BackupFixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "hbcm-original-backup-tests",
                Guid.NewGuid().ToString("N"));
            var convertedDirectory = Path.Combine(directory, "converted");
            System.IO.Directory.CreateDirectory(convertedDirectory);
            var sourcePath = Path.Combine(directory, "Source.mkv");
            var destinationPath = Path.Combine(convertedDirectory, "Output.mp4");
            var sourceBytes = CreateBytes((3 * 1024 * 1024) + 73, 17);
            var destinationBytes = CreateBytes((1024 * 1024) + 31, 93);
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            await File.WriteAllBytesAsync(destinationPath, destinationBytes);
            var completedEncode = CompletedEncodeCapture.Create(new CompletionEvent(
                sourcePath,
                destinationPath,
                convertedDirectory,
                0,
                DateTimeOffset.UtcNow));
            var databasePath = Path.Combine(directory, "history.db");
            var completedRepository = new CompletedEncodeRepository(databasePath);
            await completedRepository.InitializeAsync();
            Assert.True(await completedRepository.AddAsync(completedEncode));
            var plan = new ReplacementPreflightService().Review(completedEncode);
            Assert.True(plan.CanProceed);
            await File.WriteAllBytesAsync(plan.Paths.TemporaryPath, destinationBytes);
            var operationRepository = new ReplacementOperationRepository(databasePath);
            var created = ReplacementOperationFactory.CreatePlanned(plan, DateTimeOffset.UtcNow);
            await operationRepository.AddAsync(created);
            Assert.True(await operationRepository.UpdateStateAsync(
                created.Id,
                ReplacementOperationStatus.InProgress,
                ReplacementOperationStage.Verifying,
                destinationBytes.Length,
                ReplacementVerificationStatus.Verified,
                null,
                created.DateUpdatedUtc.AddMinutes(1)));
            var operation = Assert.IsType<ReplacementOperation>(
                await operationRepository.GetByIdAsync(created.Id));
            var backupRepository = new OriginalBackupRepository(databasePath);
            return new BackupFixture(
                directory,
                sourcePath,
                destinationPath,
                sourceBytes,
                destinationBytes,
                plan,
                operation,
                operationRepository,
                backupRepository);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }

        private static byte[] CreateBytes(int length, int seed)
        {
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }
    }
}
