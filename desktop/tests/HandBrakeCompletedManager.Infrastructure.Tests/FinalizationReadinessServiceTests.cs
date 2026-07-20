using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class FinalizationReadinessServiceTests
{
    [Fact]
    public async Task ReviewAsync_ReturnsReadyWhenBothCopiesStillMatch()
    {
        using var fixture = await ReadinessFixture.CreateAsync();

        var result = await new FinalizationReadinessService().ReviewAsync(fixture.Plan, fixture.Operation, fixture.Backup);

        Assert.True(result.IsReady);
        Assert.Empty(result.Issues);
        Assert.NotNull(result.TemporarySha256);
        Assert.NotNull(result.SourceSha256);
    }

    [Fact]
    public async Task ReviewAsync_BlocksSameSizeTemporaryTampering()
    {
        using var fixture = await ReadinessFixture.CreateAsync();
        await File.WriteAllBytesAsync(fixture.Plan.Paths.TemporaryPath, Enumerable.Repeat((byte)91, fixture.DestinationBytes.Length).ToArray());

        var result = await new FinalizationReadinessService().ReviewAsync(fixture.Plan, fixture.Operation, fixture.Backup);

        Assert.False(result.IsReady);
        Assert.Contains(result.Issues, issue => issue.Code == "TemporaryHashMismatch");
    }

    [Fact]
    public async Task ReviewAsync_BlocksSameSizeBackupTampering()
    {
        using var fixture = await ReadinessFixture.CreateAsync();
        await File.WriteAllBytesAsync(fixture.Plan.Paths.BackupPath, Enumerable.Repeat((byte)37, fixture.SourceBytes.Length).ToArray());

        var result = await new FinalizationReadinessService().ReviewAsync(fixture.Plan, fixture.Operation, fixture.Backup);

        Assert.False(result.IsReady);
        Assert.Contains(result.Issues, issue => issue.Code == "BackupHashMismatch");
    }

    [Fact]
    public async Task ReviewAsync_BlocksOccupiedFinalPathWithoutHashing()
    {
        using var fixture = await ReadinessFixture.CreateAsync();
        await File.WriteAllTextAsync(fixture.Plan.Paths.FinalPath, "conflict");

        var result = await new FinalizationReadinessService().ReviewAsync(fixture.Plan, fixture.Operation, fixture.Backup);

        Assert.False(result.IsReady);
        Assert.Contains(result.Issues, issue => issue.Code == "FinalPathConflict");
        Assert.Null(result.SourceSha256);
    }

    [Fact]
    public async Task ReviewAsync_BlocksBackupThatIsNotPersistedAsVerified()
    {
        using var fixture = await ReadinessFixture.CreateAsync();
        var unverified = fixture.Backup with { Status = OriginalBackupStatus.Verifying, Sha256 = null };

        var result = await new FinalizationReadinessService().ReviewAsync(fixture.Plan, fixture.Operation, unverified);

        Assert.False(result.IsReady);
        Assert.Contains(result.Issues, issue => issue.Code == "BackupNotVerified");
    }

    private sealed class ReadinessFixture : IDisposable
    {
        private ReadinessFixture(string directory, ReplacementPlan plan, ReplacementOperation operation,
            OriginalBackupState backup, byte[] sourceBytes, byte[] destinationBytes)
        {
            Directory = directory;
            Plan = plan;
            Operation = operation;
            Backup = backup;
            SourceBytes = sourceBytes;
            DestinationBytes = destinationBytes;
        }

        public string Directory { get; }
        public ReplacementPlan Plan { get; }
        public ReplacementOperation Operation { get; }
        public OriginalBackupState Backup { get; }
        public byte[] SourceBytes { get; }
        public byte[] DestinationBytes { get; }

        public static async Task<ReadinessFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"hbcm-readiness-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var source = Path.Combine(directory, "source.mkv");
            var destination = Path.Combine(directory, "output.mp4");
            var sourceBytes = Enumerable.Range(0, 2_048).Select(index => (byte)(index % 251)).ToArray();
            var destinationBytes = Enumerable.Range(0, 1_024).Select(index => (byte)(index % 239)).ToArray();
            await File.WriteAllBytesAsync(source, sourceBytes);
            await File.WriteAllBytesAsync(destination, destinationBytes);
            var now = DateTimeOffset.UtcNow;
            var encode = new CompletedEncode(
                Guid.NewGuid(), "fingerprint", now, source, "source.mkv", ".mkv", sourceBytes.Length, true,
                destination, "output.mp4", ".mp4", destinationBytes.Length, true,
                File.GetLastWriteTimeUtc(destination), 50, 50, sourceBytes.Length - destinationBytes.Length,
                0, "Completed", now, now);
            var paths = ReplacementPlanner.BuildPaths(encode);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(paths.BackupPath)!);
            await File.WriteAllBytesAsync(paths.TemporaryPath, destinationBytes);
            await File.WriteAllBytesAsync(paths.BackupPath, sourceBytes);
            var plan = new ReplacementPlan(encode, paths,
                new ReplacementPreflightSnapshot(true, sourceBytes.Length, true, destinationBytes.Length, false, false), []);
            var operation = new ReplacementOperation(
                Guid.NewGuid(), encode.Id, ReplacementOperationStatus.InProgress, ReplacementOperationStage.BackingUpSource,
                source, destination, paths.FinalPath, paths.TemporaryPath, paths.BackupPath,
                sourceBytes.Length, destinationBytes.Length, destinationBytes.Length,
                ReplacementVerificationStatus.Verified, null, now, now);
            var backup = new OriginalBackupState(
                operation.Id, paths.BackupPath, OriginalBackupStatus.Verified, sourceBytes.Length, sourceBytes.Length,
                Convert.ToHexString(SHA256.HashData(sourceBytes)), null, now, now);
            return new ReadinessFixture(directory, plan, operation, backup, sourceBytes, destinationBytes);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
