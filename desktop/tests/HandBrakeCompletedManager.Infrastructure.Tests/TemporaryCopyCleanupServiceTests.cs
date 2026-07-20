using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class TemporaryCopyCleanupServiceTests
{
    [Fact]
    public async Task DiscardAsync_RemovesOnlyRecordedTemporaryFileAndCancelsOperation()
    {
        var fixture = await CleanupFixture.CreateAsync(ReplacementOperationStatus.InProgress);

        try
        {
            var result = await fixture.CleanupService.DiscardAsync(fixture.Plan, fixture.Operation);
            var persisted = await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id);

            Assert.Equal(fixture.TemporaryBytes.Length, result.BytesRemoved);
            Assert.False(File.Exists(fixture.Plan.Paths.TemporaryPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
            Assert.Equal(fixture.DestinationBytes, await File.ReadAllBytesAsync(fixture.DestinationPath));
            Assert.False(File.Exists(fixture.Plan.Paths.FinalPath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.Plan.Paths.BackupPath)));
            Assert.NotNull(persisted);
            Assert.Equal(ReplacementOperationStatus.Cancelled, persisted.Status);
            Assert.Equal(ReplacementOperationStage.Cancelled, persisted.Stage);
            Assert.Equal(ReplacementVerificationStatus.NotVerified, persisted.VerificationStatus);
            Assert.Contains("discarded", persisted.FailureMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DiscardAsync_RefusesOperationThatDoesNotExactlyMatchPlan()
    {
        var fixture = await CleanupFixture.CreateAsync(ReplacementOperationStatus.Cancelled);
        var mismatched = fixture.Operation with { TemporaryPath = fixture.SourcePath };

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.CleanupService.DiscardAsync(fixture.Plan, mismatched));

            Assert.Equal(fixture.TemporaryBytes, await File.ReadAllBytesAsync(fixture.Plan.Paths.TemporaryPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DiscardAsync_RefusesTemporaryFileThatIsOpenForCopying()
    {
        var fixture = await CleanupFixture.CreateAsync(ReplacementOperationStatus.InProgress);

        try
        {
            await using var activeCopyStream = new FileStream(
                fixture.Plan.Paths.TemporaryPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            await Assert.ThrowsAsync<IOException>(() =>
                fixture.CleanupService.DiscardAsync(fixture.Plan, fixture.Operation));
            var persisted = await fixture.OperationRepository.GetByIdAsync(fixture.Operation.Id);

            Assert.True(File.Exists(fixture.Plan.Paths.TemporaryPath));
            Assert.NotNull(persisted);
            Assert.Equal(ReplacementOperationStatus.InProgress, persisted.Status);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DiscardAsync_RefusesOperationThatIsNoLongerLatest()
    {
        var fixture = await CleanupFixture.CreateAsync(ReplacementOperationStatus.Cancelled);
        var newerOperation = fixture.Operation with
        {
            Id = Guid.NewGuid(),
            DateCreatedUtc = fixture.Operation.DateCreatedUtc.AddMinutes(1),
            DateUpdatedUtc = fixture.Operation.DateUpdatedUtc.AddMinutes(1)
        };
        await fixture.OperationRepository.AddAsync(newerOperation);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.CleanupService.DiscardAsync(fixture.Plan, fixture.Operation));

            Assert.True(File.Exists(fixture.Plan.Paths.TemporaryPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DiscardAsync_RefusesDirectoryAtTemporaryPath()
    {
        var fixture = await CleanupFixture.CreateAsync(ReplacementOperationStatus.Cancelled);
        File.Delete(fixture.Plan.Paths.TemporaryPath);
        System.IO.Directory.CreateDirectory(fixture.Plan.Paths.TemporaryPath);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.CleanupService.DiscardAsync(fixture.Plan, fixture.Operation));

            Assert.True(System.IO.Directory.Exists(fixture.Plan.Paths.TemporaryPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DiscardAsync_RefusesMissingTemporaryFile()
    {
        var fixture = await CleanupFixture.CreateAsync(ReplacementOperationStatus.Cancelled);
        File.Delete(fixture.Plan.Paths.TemporaryPath);

        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                fixture.CleanupService.DiscardAsync(fixture.Plan, fixture.Operation));

            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
            Assert.Equal(fixture.DestinationBytes, await File.ReadAllBytesAsync(fixture.DestinationPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DiscardAsync_RefusesCompletedOperation()
    {
        var fixture = await CleanupFixture.CreateAsync(ReplacementOperationStatus.Cancelled);
        var completed = fixture.Operation with
        {
            Status = ReplacementOperationStatus.Completed,
            Stage = ReplacementOperationStage.Completed,
            VerificationStatus = ReplacementVerificationStatus.Verified
        };

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.CleanupService.DiscardAsync(fixture.Plan, completed));

            Assert.True(File.Exists(fixture.Plan.Paths.TemporaryPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DiscardAsync_RefusesStateChangedSinceReview()
    {
        var fixture = await CleanupFixture.CreateAsync(ReplacementOperationStatus.Cancelled);
        Assert.True(await fixture.OperationRepository.UpdateStateAsync(
            fixture.Operation.Id,
            ReplacementOperationStatus.Failed,
            ReplacementOperationStage.Failed,
            fixture.Operation.BytesCopied,
            ReplacementVerificationStatus.Failed,
            "State changed.",
            fixture.Operation.DateUpdatedUtc.AddMinutes(1)));

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.CleanupService.DiscardAsync(fixture.Plan, fixture.Operation));

            Assert.True(File.Exists(fixture.Plan.Paths.TemporaryPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private sealed class CleanupFixture : IDisposable
    {
        private CleanupFixture(
            string directory,
            string sourcePath,
            string destinationPath,
            byte[] sourceBytes,
            byte[] destinationBytes,
            byte[] temporaryBytes,
            ReplacementPlan plan,
            ReplacementOperation operation,
            ReplacementOperationRepository operationRepository)
        {
            Directory = directory;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
            SourceBytes = sourceBytes;
            DestinationBytes = destinationBytes;
            TemporaryBytes = temporaryBytes;
            Plan = plan;
            Operation = operation;
            OperationRepository = operationRepository;
            CleanupService = new TemporaryCopyCleanupService(operationRepository);
        }

        public string Directory { get; }
        public string SourcePath { get; }
        public string DestinationPath { get; }
        public byte[] SourceBytes { get; }
        public byte[] DestinationBytes { get; }
        public byte[] TemporaryBytes { get; }
        public ReplacementPlan Plan { get; }
        public ReplacementOperation Operation { get; }
        public ReplacementOperationRepository OperationRepository { get; }
        public TemporaryCopyCleanupService CleanupService { get; }

        public static async Task<CleanupFixture> CreateAsync(ReplacementOperationStatus status)
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "hbcm-temporary-cleanup-tests",
                Guid.NewGuid().ToString("N"));
            var convertedDirectory = Path.Combine(directory, "converted");
            System.IO.Directory.CreateDirectory(convertedDirectory);
            var sourcePath = Path.Combine(directory, "Source.mkv");
            var destinationPath = Path.Combine(convertedDirectory, "Output.mp4");
            var sourceBytes = CreateBytes(4096, 17);
            var destinationBytes = CreateBytes(2048, 93);
            var temporaryBytes = CreateBytes(1024, 51);
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
            await File.WriteAllBytesAsync(plan.Paths.TemporaryPath, temporaryBytes);
            var now = DateTimeOffset.UtcNow;
            var operation = ReplacementOperationFactory.CreatePlanned(plan, now) with
            {
                Status = status,
                Stage = status switch
                {
                    ReplacementOperationStatus.Cancelled => ReplacementOperationStage.Cancelled,
                    ReplacementOperationStatus.Failed => ReplacementOperationStage.Failed,
                    _ => ReplacementOperationStage.Copying
                },
                BytesCopied = temporaryBytes.Length,
                VerificationStatus = status == ReplacementOperationStatus.Failed
                    ? ReplacementVerificationStatus.Failed
                    : ReplacementVerificationStatus.NotVerified,
                FailureMessage = status == ReplacementOperationStatus.Failed ? "Copy failed." : null
            };
            var operationRepository = new ReplacementOperationRepository(databasePath);
            await operationRepository.AddAsync(operation);
            return new CleanupFixture(
                directory,
                sourcePath,
                destinationPath,
                sourceBytes,
                destinationBytes,
                temporaryBytes,
                plan,
                operation,
                operationRepository);
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
