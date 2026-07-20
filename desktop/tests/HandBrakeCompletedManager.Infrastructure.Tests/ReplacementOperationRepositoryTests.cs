using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class ReplacementOperationRepositoryTests
{
    [Fact]
    public async Task AddAndUpdate_PersistsIncompleteRecoveryState()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "hbcm-replacement-operation-tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(testDirectory, "history.db");
        var completedRepository = new CompletedEncodeRepository(databasePath);
        var operationRepository = new ReplacementOperationRepository(databasePath);
        var completedEncode = CreateCompletedEncode();
        var createdAt = new DateTimeOffset(2026, 7, 20, 2, 0, 0, TimeSpan.Zero);
        var operation = new ReplacementOperation(
            Guid.NewGuid(),
            completedEncode.Id,
            ReplacementOperationStatus.Planned,
            ReplacementOperationStage.Preparing,
            completedEncode.SourcePath,
            completedEncode.DestinationPath,
            @"D:\Videos\Source.mp4",
            @"D:\Videos\Source.mp4.hbcm-copying",
            @"D:\Videos\HandBrake Original Backup\Source.mkv",
            1_000,
            400,
            0,
            ReplacementVerificationStatus.NotVerified,
            null,
            createdAt,
            createdAt);

        try
        {
            await completedRepository.InitializeAsync();
            Assert.True(await completedRepository.AddAsync(completedEncode));
            await operationRepository.AddAsync(operation);

            var updated = await operationRepository.UpdateStateAsync(
                operation.Id,
                ReplacementOperationStatus.InProgress,
                ReplacementOperationStage.Copying,
                250,
                ReplacementVerificationStatus.NotVerified,
                null,
                createdAt.AddMinutes(1));
            await Assert.ThrowsAsync<SqliteException>(() => operationRepository.UpdateStateAsync(
                operation.Id,
                ReplacementOperationStatus.InProgress,
                ReplacementOperationStage.Copying,
                401,
                ReplacementVerificationStatus.NotVerified,
                null,
                createdAt.AddMinutes(2)));
            await Assert.ThrowsAsync<SqliteException>(() => operationRepository.AddAsync(
                operation with { Id = Guid.NewGuid(), DateUpdatedUtc = createdAt.AddMinutes(2) }));
            var persisted = Assert.Single(await operationRepository.GetIncompleteAsync());

            Assert.True(updated);
            Assert.Equal(ReplacementOperationStatus.InProgress, persisted.Status);
            Assert.Equal(ReplacementOperationStage.Copying, persisted.Stage);
            Assert.Equal(250, persisted.BytesCopied);
            Assert.Equal(operation.TemporaryPath, persisted.TemporaryPath);
            Assert.Equal(operation.BackupPath, persisted.BackupPath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetIncompleteAsync_ExcludesCompletedAndCancelledOperations()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "hbcm-replacement-operation-tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(testDirectory, "history.db");
        var completedRepository = new CompletedEncodeRepository(databasePath);
        var operationRepository = new ReplacementOperationRepository(databasePath);
        var completedEncode = CreateCompletedEncode();
        var createdAt = new DateTimeOffset(2026, 7, 20, 2, 0, 0, TimeSpan.Zero);

        try
        {
            await completedRepository.InitializeAsync();
            Assert.True(await completedRepository.AddAsync(completedEncode));

            foreach (var status in new[]
                     {
                         ReplacementOperationStatus.Planned,
                         ReplacementOperationStatus.Completed,
                         ReplacementOperationStatus.Cancelled
                     })
            {
                var terminalStage = status switch
                {
                    ReplacementOperationStatus.Completed => ReplacementOperationStage.Completed,
                    ReplacementOperationStatus.Cancelled => ReplacementOperationStage.Cancelled,
                    _ => ReplacementOperationStage.Preparing
                };
                await operationRepository.AddAsync(new ReplacementOperation(
                    Guid.NewGuid(),
                    completedEncode.Id,
                    status,
                    terminalStage,
                    completedEncode.SourcePath,
                    completedEncode.DestinationPath,
                    @"D:\Videos\Source.mp4",
                    @"D:\Videos\Source.mp4.hbcm-copying",
                    @"D:\Videos\HandBrake Original Backup\Source.mkv",
                    1_000,
                    400,
                    0,
                    status == ReplacementOperationStatus.Completed
                        ? ReplacementVerificationStatus.Verified
                        : ReplacementVerificationStatus.NotVerified,
                    null,
                    createdAt,
                    createdAt));
            }

            var incomplete = Assert.Single(await operationRepository.GetIncompleteAsync());

            Assert.Equal(ReplacementOperationStatus.Planned, incomplete.Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AddAsync_AllowsPlannedRetryAfterFailedOperation()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "hbcm-replacement-operation-tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(testDirectory, "history.db");
        var completedRepository = new CompletedEncodeRepository(databasePath);
        var operationRepository = new ReplacementOperationRepository(databasePath);
        var completedEncode = CreateCompletedEncode();
        var createdAt = new DateTimeOffset(2026, 7, 20, 2, 0, 0, TimeSpan.Zero);

        try
        {
            await completedRepository.InitializeAsync();
            Assert.True(await completedRepository.AddAsync(completedEncode));
            var failed = new ReplacementOperation(
                Guid.NewGuid(),
                completedEncode.Id,
                ReplacementOperationStatus.Failed,
                ReplacementOperationStage.Failed,
                completedEncode.SourcePath,
                completedEncode.DestinationPath,
                @"D:\Videos\Source.mp4",
                @"D:\Videos\Source.mp4.hbcm-copying",
                @"D:\Videos\HandBrake Original Backup\Source.mkv",
                1_000,
                400,
                0,
                ReplacementVerificationStatus.Failed,
                "Insufficient space.",
                createdAt,
                createdAt);
            await operationRepository.AddAsync(failed);

            var retry = failed with
            {
                Id = Guid.NewGuid(),
                Status = ReplacementOperationStatus.Planned,
                Stage = ReplacementOperationStage.Preparing,
                VerificationStatus = ReplacementVerificationStatus.NotVerified,
                FailureMessage = null,
                DateCreatedUtc = createdAt.AddMinutes(1),
                DateUpdatedUtc = createdAt.AddMinutes(1)
            };
            await operationRepository.AddAsync(retry);

            Assert.Equal(retry.Id, (await operationRepository.GetLatestForCompletedEncodeAsync(
                completedEncode.Id))?.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    private static CompletedEncode CreateCompletedEncode()
    {
        var timestamp = new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);
        return new CompletedEncode(
            Guid.NewGuid(),
            "REPLACEMENT-OPERATION-TEST",
            timestamp,
            @"D:\Videos\Source.mkv",
            "Source.mkv",
            ".mkv",
            1_000,
            true,
            @"E:\Converted\Output.mp4",
            "Output.mp4",
            ".mp4",
            400,
            true,
            timestamp,
            40,
            60,
            600,
            0,
            "Completed",
            timestamp,
            timestamp);
    }
}
