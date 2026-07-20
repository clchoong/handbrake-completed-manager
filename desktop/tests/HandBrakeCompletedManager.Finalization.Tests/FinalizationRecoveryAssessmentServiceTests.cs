using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Finalization.Tests;

public sealed class FinalizationRecoveryAssessmentServiceTests
{
    [Fact]
    public async Task ReviewAsync_RecognizesConsistentPreparedArtifacts()
    {
        using var fixture = await ArtifactFixture.CreateAsync(FinalizationCheckpoint.Prepared, keepTemporary: true, createFinal: false);

        var decision = await new FinalizationRecoveryAssessmentService().ReviewAsync(fixture.Operation, fixture.Transaction);

        Assert.True(decision.IsConsistent);
        Assert.Equal(FinalizationRecoveryAction.BeginPromotion, decision.Action);
    }

    [Fact]
    public async Task ReviewAsync_RecognizesPromotionCompletedBeforeCheckpointWrite()
    {
        using var fixture = await ArtifactFixture.CreateAsync(
            FinalizationCheckpoint.PromoteTemporaryIntentRecorded,
            keepTemporary: false,
            createFinal: true);

        var decision = await new FinalizationRecoveryAssessmentService().ReviewAsync(fixture.Operation, fixture.Transaction);

        Assert.True(decision.IsConsistent);
        Assert.Equal(FinalizationRecoveryAction.RecordFinalPromoted, decision.Action);
    }

    [Fact]
    public async Task ReviewAsync_RefusesCorruptedBackup()
    {
        using var fixture = await ArtifactFixture.CreateAsync(FinalizationCheckpoint.Prepared, keepTemporary: true, createFinal: false);
        await File.WriteAllBytesAsync(fixture.Operation.BackupPath, new byte[] { 9, 9, 9 });

        var decision = await new FinalizationRecoveryAssessmentService().ReviewAsync(fixture.Operation, fixture.Transaction);

        Assert.False(decision.IsConsistent);
        Assert.Equal(FinalizationRecoveryAction.ManualReview, decision.Action);
    }

    [Fact]
    public async Task ReviewAsync_RefusesLockedArtifactInsteadOfGuessing()
    {
        using var fixture = await ArtifactFixture.CreateAsync(FinalizationCheckpoint.Prepared, keepTemporary: true, createFinal: false);
        using var lockStream = new FileStream(fixture.Operation.SourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var decision = await new FinalizationRecoveryAssessmentService().ReviewAsync(fixture.Operation, fixture.Transaction);

        Assert.False(decision.IsConsistent);
        Assert.Equal(FinalizationRecoveryAction.ManualReview, decision.Action);
        Assert.Contains("could not be inspected", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ArtifactFixture : IDisposable
    {
        private ArtifactFixture(string directory, ReplacementOperation operation, FinalizationTransaction transaction)
        {
            Directory = directory;
            Operation = operation;
            Transaction = transaction;
        }

        public string Directory { get; }
        public ReplacementOperation Operation { get; }
        public FinalizationTransaction Transaction { get; }

        public static async Task<ArtifactFixture> CreateAsync(
            FinalizationCheckpoint checkpoint,
            bool keepTemporary,
            bool createFinal)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"hbcm-recovery-assessment-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "source.mkv");
            var temporaryPath = Path.Combine(directory, "source.mp4.hbcm-copying");
            var finalPath = Path.Combine(directory, "source.mp4");
            var backupPath = Path.Combine(directory, "backup", "source.mkv");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            var sourceBytes = Enumerable.Range(0, 2_048).Select(value => (byte)(value % 251)).ToArray();
            var finalBytes = Enumerable.Range(0, 1_024).Select(value => (byte)(value % 239)).ToArray();
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            await File.WriteAllBytesAsync(backupPath, sourceBytes);
            if (keepTemporary)
            {
                await File.WriteAllBytesAsync(temporaryPath, finalBytes);
            }
            if (createFinal)
            {
                await File.WriteAllBytesAsync(finalPath, finalBytes);
            }

            var now = DateTimeOffset.UtcNow;
            var operation = new ReplacementOperation(
                Guid.NewGuid(), Guid.NewGuid(), ReplacementOperationStatus.InProgress,
                ReplacementOperationStage.BackingUpSource,
                sourcePath, Path.Combine(directory, "converted.mp4"), finalPath, temporaryPath, backupPath,
                sourceBytes.Length, finalBytes.Length, finalBytes.Length,
                ReplacementVerificationStatus.Verified, null, now, now);
            var transaction = new FinalizationTransaction(
                operation.Id,
                checkpoint,
                Convert.ToHexString(SHA256.HashData(sourceBytes)),
                Convert.ToHexString(SHA256.HashData(finalBytes)),
                0,
                null,
                now,
                now);
            return new ArtifactFixture(directory, operation, transaction);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
