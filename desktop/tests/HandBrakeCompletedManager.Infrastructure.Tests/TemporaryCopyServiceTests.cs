using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class TemporaryCopyServiceTests
{
    [Fact]
    public async Task CopyAndVerifyAsync_CreatesVerifiedTemporaryCopyAndPreservesOriginalFiles()
    {
        var fixture = await TemporaryCopyFixture.CreateAsync();
        var progressUpdates = new List<ReplacementCopyProgress>();
        var service = new TemporaryCopyService(
            fixture.OperationRepository,
            new FixedAvailableSpaceProvider(long.MaxValue));

        try
        {
            var result = await service.CopyAndVerifyAsync(
                fixture.Plan,
                new InlineProgress<ReplacementCopyProgress>(progressUpdates.Add));
            var operation = await fixture.OperationRepository.GetByIdAsync(result.OperationId);

            Assert.NotNull(operation);
            Assert.Equal(ReplacementOperationStatus.InProgress, operation.Status);
            Assert.Equal(ReplacementOperationStage.Verifying, operation.Stage);
            Assert.Equal(ReplacementVerificationStatus.Verified, operation.VerificationStatus);
            Assert.Equal(fixture.DestinationBytes.Length, operation.BytesCopied);
            Assert.Equal(fixture.DestinationBytes.Length, result.BytesCopied);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(fixture.DestinationBytes)),
                result.Sha256);
            Assert.Equal(fixture.DestinationBytes, await File.ReadAllBytesAsync(result.TemporaryPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
            Assert.Equal(fixture.DestinationBytes, await File.ReadAllBytesAsync(fixture.DestinationPath));
            Assert.NotEmpty(progressUpdates);
            Assert.Equal(fixture.DestinationBytes.Length, progressUpdates[^1].BytesCopied);
            Assert.Equal(100, progressUpdates[^1].Percentage, precision: 6);
            Assert.False(File.Exists(fixture.Plan.Paths.FinalPath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.Plan.Paths.BackupPath)));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CopyAndVerifyAsync_RejectsInsufficientSpaceBeforeCreatingTemporaryFile()
    {
        var fixture = await TemporaryCopyFixture.CreateAsync();
        var service = new TemporaryCopyService(
            fixture.OperationRepository,
            new FixedAvailableSpaceProvider(fixture.DestinationBytes.Length - 1));

        try
        {
            var exception = await Assert.ThrowsAsync<InsufficientDiskSpaceException>(() =>
                service.CopyAndVerifyAsync(fixture.Plan));
            var operation = await fixture.OperationRepository.GetLatestForCompletedEncodeAsync(
                fixture.CompletedEncode.Id);

            Assert.Equal(fixture.DestinationBytes.Length, exception.RequiredBytes);
            Assert.NotNull(operation);
            Assert.Equal(ReplacementOperationStatus.Failed, operation.Status);
            Assert.Equal(ReplacementOperationStage.Failed, operation.Stage);
            Assert.Equal(0, operation.BytesCopied);
            Assert.False(File.Exists(fixture.Plan.Paths.TemporaryPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CopyAndVerifyAsync_PersistsCancellationWithoutTouchingSource()
    {
        var fixture = await TemporaryCopyFixture.CreateAsync();
        var service = new TemporaryCopyService(
            fixture.OperationRepository,
            new FixedAvailableSpaceProvider(long.MaxValue));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.CopyAndVerifyAsync(fixture.Plan, cancellationToken: cancellation.Token));
            var operation = await fixture.OperationRepository.GetLatestForCompletedEncodeAsync(
                fixture.CompletedEncode.Id);

            Assert.NotNull(operation);
            Assert.Equal(ReplacementOperationStatus.Cancelled, operation.Status);
            Assert.Equal(ReplacementOperationStage.Cancelled, operation.Stage);
            Assert.Equal(0, operation.BytesCopied);
            Assert.False(File.Exists(fixture.Plan.Paths.TemporaryPath));
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CopyAndVerifyAsync_RetainsPartialCopyAndProgressWhenCancelledDuringCopy()
    {
        var fixture = await TemporaryCopyFixture.CreateAsync();
        var service = new TemporaryCopyService(
            fixture.OperationRepository,
            new FixedAvailableSpaceProvider(long.MaxValue));
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<ReplacementCopyProgress>(_ => cancellation.Cancel());

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.CopyAndVerifyAsync(fixture.Plan, progress, cancellation.Token));
            var operation = await fixture.OperationRepository.GetLatestForCompletedEncodeAsync(
                fixture.CompletedEncode.Id);
            var partialLength = new FileInfo(fixture.Plan.Paths.TemporaryPath).Length;

            Assert.NotNull(operation);
            Assert.Equal(ReplacementOperationStatus.Cancelled, operation.Status);
            Assert.Equal(ReplacementOperationStage.Cancelled, operation.Stage);
            Assert.True(partialLength > 0);
            Assert.True(partialLength < fixture.DestinationBytes.Length);
            Assert.Equal(partialLength, operation.BytesCopied);
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
            Assert.Equal(fixture.DestinationBytes, await File.ReadAllBytesAsync(fixture.DestinationPath));
            Assert.False(File.Exists(fixture.Plan.Paths.FinalPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CopyAndVerifyAsync_RejectsStalePlanAndDoesNotOverwriteTemporaryFile()
    {
        var fixture = await TemporaryCopyFixture.CreateAsync();
        await File.WriteAllTextAsync(fixture.Plan.Paths.TemporaryPath, "existing-partial-copy");
        var service = new TemporaryCopyService(
            fixture.OperationRepository,
            new FixedAvailableSpaceProvider(long.MaxValue));

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CopyAndVerifyAsync(fixture.Plan));

            Assert.Equal(
                "existing-partial-copy",
                await File.ReadAllTextAsync(fixture.Plan.Paths.TemporaryPath));
            Assert.Null(await fixture.OperationRepository.GetLatestForCompletedEncodeAsync(
                fixture.CompletedEncode.Id));
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

    private sealed class TemporaryCopyFixture : IDisposable
    {
        private TemporaryCopyFixture(
            string directory,
            string sourcePath,
            string destinationPath,
            byte[] sourceBytes,
            byte[] destinationBytes,
            CompletedEncode completedEncode,
            ReplacementPlan plan,
            ReplacementOperationRepository operationRepository)
        {
            Directory = directory;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
            SourceBytes = sourceBytes;
            DestinationBytes = destinationBytes;
            CompletedEncode = completedEncode;
            Plan = plan;
            OperationRepository = operationRepository;
        }

        public string Directory { get; }
        public string SourcePath { get; }
        public string DestinationPath { get; }
        public byte[] SourceBytes { get; }
        public byte[] DestinationBytes { get; }
        public CompletedEncode CompletedEncode { get; }
        public ReplacementPlan Plan { get; }
        public ReplacementOperationRepository OperationRepository { get; }

        public static async Task<TemporaryCopyFixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "hbcm-temporary-copy-tests",
                Guid.NewGuid().ToString("N"));
            var convertedDirectory = Path.Combine(directory, "converted");
            System.IO.Directory.CreateDirectory(convertedDirectory);
            var sourcePath = Path.Combine(directory, "Source.mkv");
            var destinationPath = Path.Combine(convertedDirectory, "Output.mp4");
            var sourceBytes = CreateBytes(3 * 1024 * 1024, 17);
            var destinationBytes = CreateBytes((2 * 1024 * 1024) + 137, 93);
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
            var operationRepository = new ReplacementOperationRepository(databasePath);
            var plan = new ReplacementPreflightService().Review(completedEncode);
            Assert.True(plan.CanProceed);
            return new TemporaryCopyFixture(
                directory,
                sourcePath,
                destinationPath,
                sourceBytes,
                destinationBytes,
                completedEncode,
                plan,
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
