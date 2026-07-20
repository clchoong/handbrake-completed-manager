using System.Text.Json;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class HandBrakeLogImportServiceTests
{
    [Fact]
    public async Task ReviewAndImportAsync_ImportsOnlySuccessfulLogWithExistingOutput()
    {
        var testDirectory = CreateTestDirectory();
        try
        {
            var sourcePath = Path.Combine(testDirectory, "source.mkv");
            var outputPath = Path.Combine(testDirectory, "output.mp4");
            await File.WriteAllTextAsync(sourcePath, "source");
            await File.WriteAllTextAsync(outputPath, "output");
            var logDirectory = Path.Combine(testDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(logDirectory, "completed.txt"),
                CreateCompletedLog(sourcePath, outputPath));
            await File.WriteAllTextAsync(
                Path.Combine(logDirectory, "paused.txt"),
                "# Starting Encode ...\n# Encode Paused");

            var repository = new CompletedEncodeRepository(Path.Combine(testDirectory, "history.db"));
            var service = new HandBrakeLogImportService(repository);

            var review = await service.ReviewAsync(logDirectory);
            var result = await service.ImportAsync(review.Items);
            var records = await repository.GetAllAsync();

            Assert.Equal(2, review.Items.Count);
            Assert.Equal(1, review.RecoverableCount);
            Assert.Equal(1, result.Imported);
            Assert.Equal(0, result.Duplicates);
            var record = Assert.Single(records);
            Assert.Equal(sourcePath, record.SourcePath);
            Assert.Equal(outputPath, record.DestinationPath);
        }
        finally
        {
            Cleanup(testDirectory);
        }
    }

    [Fact]
    public async Task ReviewAsync_SkipsMissingOutputsAndExistingHistory()
    {
        var testDirectory = CreateTestDirectory();
        try
        {
            var sourcePath = Path.Combine(testDirectory, "source.mkv");
            var outputPath = Path.Combine(testDirectory, "output.mp4");
            await File.WriteAllTextAsync(sourcePath, "source");
            await File.WriteAllTextAsync(outputPath, "output");
            var logDirectory = Path.Combine(testDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            var completedLogPath = Path.Combine(logDirectory, "completed.txt");
            await File.WriteAllTextAsync(completedLogPath, CreateCompletedLog(sourcePath, outputPath));

            var repository = new CompletedEncodeRepository(Path.Combine(testDirectory, "history.db"));
            var service = new HandBrakeLogImportService(repository);
            var firstReview = await service.ReviewAsync(logDirectory);
            Assert.Equal(1, (await service.ImportAsync(firstReview.Items)).Imported);

            var duplicateReview = await service.ReviewAsync(logDirectory);
            Assert.Equal(0, duplicateReview.RecoverableCount);
            Assert.Contains("Already", Assert.Single(duplicateReview.Items).Status);

            File.Delete(outputPath);
            var missingOutputReview = await service.ReviewAsync(logDirectory);
            Assert.Equal(0, missingOutputReview.RecoverableCount);
            Assert.Contains("output file no longer exists", Assert.Single(missingOutputReview.Items).Status);
        }
        finally
        {
            Cleanup(testDirectory);
        }
    }

    private static string CreateCompletedLog(string sourcePath, string outputPath) => $$"""
          "Destination": { "File": {{JsonSerializer.Serialize(outputPath)}} },
          "Source": { "Path": {{JsonSerializer.Serialize(sourcePath)}} }
        [12:00:00] Finished work at: Mon Jul 20 12:00:00 2026
        [12:00:00] libhb: work result = 0
        # Job Completed!
        """;

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "handbrake-completed-manager-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string testDirectory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}
