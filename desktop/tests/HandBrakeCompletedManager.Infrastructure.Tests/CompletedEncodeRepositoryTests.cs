using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class CompletedEncodeRepositoryTests
{
    [Fact]
    public async Task AddAsync_PreventsDuplicateFingerprintAndPersistsRecord()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "handbrake-completed-manager-tests",
            Guid.NewGuid().ToString("N"));
        var repository = new CompletedEncodeRepository(Path.Combine(testDirectory, "history.db"));
        var completedAt = new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);
        var record = new CompletedEncode(
            Guid.NewGuid(),
            "FINGERPRINT",
            completedAt,
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
            completedAt,
            40,
            60,
            600,
            0,
            "Completed",
            completedAt,
            completedAt);

        try
        {
            await repository.InitializeAsync();

            var firstInsert = await repository.AddAsync(record);
            var duplicateInsert = await repository.AddAsync(record with { Id = Guid.NewGuid() });
            var records = await repository.GetAllAsync();

            Assert.True(firstInsert);
            Assert.False(duplicateInsert);
            var persisted = Assert.Single(records);
            Assert.Equal(record.SourcePath, persisted.SourcePath);
            Assert.Equal(record.SpaceSavedBytes, persisted.SpaceSavedBytes);
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
}
