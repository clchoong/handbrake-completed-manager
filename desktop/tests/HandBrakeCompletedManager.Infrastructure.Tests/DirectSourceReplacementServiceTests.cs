using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class DirectSourceReplacementServiceTests
{
    [Theory]
    [InlineData(".avi", ".mp4")]
    [InlineData(".mp4", ".mkv")]
    [InlineData(".mkv", ".webm")]
    [InlineData(".avi", ".m4v")]
    [InlineData(".mkv", ".mkv")]
    public async Task ReplaceAsync_SupportsMatchingAndDifferentFormats(string sourceExtension, string outputExtension)
    {
        using var fixture = await Fixture.CreateAsync(sourceExtension, outputExtension);

        var result = await fixture.Service.ReplaceAsync(fixture.Record, keepOutput: false);

        Assert.Equal(fixture.ReplacementPath, result.ReplacementPath, ignoreCase: true);
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(result.ReplacementPath));
        Assert.False(File.Exists(fixture.OutputPath));
        if (!string.Equals(sourceExtension, outputExtension, StringComparison.OrdinalIgnoreCase))
            Assert.False(File.Exists(fixture.SourcePath));
    }

    [Fact]
    public async Task ReplaceAsync_EmptyOutputAtFinalPathDoesNotDeleteSource()
    {
        using var fixture = await Fixture.CreateAsync(".avi", ".mp4", outputAlreadyAtReplacementPath: true);
        await File.WriteAllBytesAsync(fixture.OutputPath, []);

        await Assert.ThrowsAsync<IOException>(() => fixture.Service.ReplaceAsync(fixture.Record, keepOutput: false));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        Assert.True(File.Exists(fixture.OutputPath));
    }

    [Fact]
    public async Task ReplaceAsync_FinalPathAppearingDuringTransferPreservesSourceAndRestoresOutput()
    {
        using var fixture = await Fixture.CreateAsync(".avi", ".mp4");
        var unrelated = new byte[] { 4, 5, 6 };
        var progress = new SynchronousProgress(value =>
        {
            if (value.Stage == DirectReplacementStage.Installing)
                File.WriteAllBytes(fixture.ReplacementPath, unrelated);
        });

        await Assert.ThrowsAsync<IOException>(() => fixture.Service.ReplaceAsync(fixture.Record, keepOutput: false, progress));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(fixture.OutputPath));
        Assert.Equal(unrelated, await File.ReadAllBytesAsync(fixture.ReplacementPath));
    }

    private sealed class SynchronousProgress(Action<DirectReplacementProgress> report) : IProgress<DirectReplacementProgress>
    {
        public void Report(DirectReplacementProgress value) => report(value);
    }

    [Fact]
    public async Task ReplaceAsync_KeepOutputAndLockedSourcePreservesBothCopies()
    {
        using var fixture = await Fixture.CreateAsync(".avi", ".mp4");
        await using var heldSource = new FileStream(fixture.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        await Assert.ThrowsAsync<IOException>(() => fixture.Service.ReplaceAsync(fixture.Record, keepOutput: true));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(fixture.OutputPath));
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(fixture.ReplacementPath));
    }

    [Fact]
    public async Task ReplaceAsync_ChangedSourceIsNotDeletedAndOutputIsRestored()
    {
        using var fixture = await Fixture.CreateAsync(".avi", ".mp4");
        var changedSource = new byte[] { 1, 9, 2 };
        var progress = new SynchronousProgress(value =>
        {
            if (value.Stage == DirectReplacementStage.Installing)
                File.WriteAllBytes(fixture.SourcePath, changedSource);
        });

        await Assert.ThrowsAsync<IOException>(() => fixture.Service.ReplaceAsync(fixture.Record, keepOutput: false, progress));
        Assert.Equal(changedSource, await File.ReadAllBytesAsync(fixture.SourcePath));
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(fixture.OutputPath));
        Assert.False(File.Exists(fixture.ReplacementPath));
    }

    [Fact]
    public async Task ReplaceAsync_DifferentExtensionLockedSourceRestoresMovedOutput()
    {
        using var fixture = await Fixture.CreateAsync(".avi", ".mp4");
        await using (var heldSource = new FileStream(fixture.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Assert.ThrowsAsync<IOException>(() => fixture.Service.ReplaceAsync(fixture.Record, keepOutput: false));
            Assert.Contains("Replacement stopped", error.Message);
            Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
            Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(fixture.OutputPath));
            Assert.False(File.Exists(fixture.ReplacementPath));
        }

        var retry = await fixture.Service.ReplaceAsync(fixture.Record, keepOutput: false);
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(retry.ReplacementPath));
        Assert.False(File.Exists(fixture.SourcePath));
    }

    [Fact]
    public async Task ReplaceAsync_ExistingTemporaryFileIsNeverDeletedOnRetry()
    {
        using var fixture = await Fixture.CreateAsync(".avi", ".mp4");
        var temporary = $"{fixture.ReplacementPath}.{fixture.Record.Id:N}.hbcm-direct";
        var existingBytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(temporary, existingBytes);

        var error = await Assert.ThrowsAsync<IOException>(() => fixture.Service.ReplaceAsync(fixture.Record, keepOutput: false));
        Assert.Contains("needs review", error.Message);
        Assert.Equal(existingBytes, await File.ReadAllBytesAsync(temporary));
        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(fixture.OutputPath));
    }

    [Fact]
    public async Task ReplaceAsync_MissingSourceAndOccupiedFinalNeverDeletesOutput()
    {
        using var fixture = await Fixture.CreateAsync(".avi", ".mp4");
        File.Delete(fixture.SourcePath);
        var unrelated = new byte[] { 9, 8, 7 };
        await File.WriteAllBytesAsync(fixture.ReplacementPath, unrelated);

        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.Service.ReplaceAsync(fixture.Record, keepOutput: false));
        Assert.Equal(unrelated, await File.ReadAllBytesAsync(fixture.ReplacementPath));
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(fixture.OutputPath));
    }

    [Fact]
    public async Task ReplaceAsync_DefaultMovesOutputAndPermanentlyReplacesSource()
    {
        using var fixture = await Fixture.CreateAsync(".mkv", ".mp4");

        var result = await fixture.Service.ReplaceAsync(fixture.Record, keepOutput: false);

        Assert.False(result.OutputKept);
        Assert.False(File.Exists(fixture.SourcePath));
        Assert.False(File.Exists(fixture.OutputPath));
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(result.ReplacementPath));
        var persisted = Assert.Single(await fixture.Repository.GetAllAsync());
        Assert.Equal("Source Replaced", persisted.FileActionStatus);
        Assert.Equal(result.ReplacementPath, persisted.ReplacementPath, ignoreCase: true);
    }

    [Fact]
    public async Task ReplaceAsync_KeepOutputCopiesAndLeavesOutputAvailable()
    {
        using var fixture = await Fixture.CreateAsync(".mkv", ".mp4");

        var result = await fixture.Service.ReplaceAsync(fixture.Record, keepOutput: true);

        Assert.True(result.OutputKept);
        Assert.False(File.Exists(fixture.SourcePath));
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(fixture.OutputPath));
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(result.ReplacementPath));
        Assert.Equal("Source Replaced, Output Kept", Assert.Single(await fixture.Repository.GetAllAsync()).FileActionStatus);
    }

    [Fact]
    public async Task ReplaceAsync_SameExtensionAtomicallyConsumesOutput()
    {
        using var fixture = await Fixture.CreateAsync(".mp4", ".mp4");

        var result = await fixture.Service.ReplaceAsync(fixture.Record, keepOutput: false);

        Assert.Equal(fixture.SourcePath, result.ReplacementPath, ignoreCase: true);
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        Assert.False(File.Exists(fixture.OutputPath));
        Assert.Equal("Source Replaced", Assert.Single(await fixture.Repository.GetAllAsync()).FileActionStatus);
    }

    [Fact]
    public async Task ReplaceAsync_CancelledCopyPreservesSourceAndOutputAndCanRetry()
    {
        using var fixture = await Fixture.CreateAsync(".mkv", ".mp4");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.ReplaceAsync(fixture.Record, keepOutput: true, cancellationToken: cancellation.Token));

        Assert.Equal(fixture.SourceBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(fixture.OutputPath));
        Assert.False(File.Exists($"{fixture.ReplacementPath}.{fixture.Record.Id:N}.hbcm-direct"));

        var retry = await fixture.Service.ReplaceAsync(fixture.Record, keepOutput: true);
        Assert.True(retry.OutputKept);
    }

    [Fact]
    public async Task ReplaceAsync_UsesOutputAlreadyAtFinalPathWithoutCopyingOverIt()
    {
        using var fixture = await Fixture.CreateAsync(".mkv", ".mp4", outputAlreadyAtReplacementPath: true);

        var result = await fixture.Service.ReplaceAsync(fixture.Record, keepOutput: false);

        Assert.False(result.OutputKept);
        Assert.False(File.Exists(fixture.SourcePath));
        Assert.Equal(fixture.OutputPath, result.ReplacementPath, ignoreCase: true);
        Assert.Equal(fixture.OutputBytes, await File.ReadAllBytesAsync(result.ReplacementPath));
        Assert.Equal("Source Replaced", Assert.Single(await fixture.Repository.GetAllAsync()).FileActionStatus);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            string directory,
            string sourcePath,
            string outputPath,
            string replacementPath,
            byte[] sourceBytes,
            byte[] outputBytes,
            CompletedEncode record,
            CompletedEncodeRepository repository)
        {
            Directory = directory;
            SourcePath = sourcePath;
            OutputPath = outputPath;
            ReplacementPath = replacementPath;
            SourceBytes = sourceBytes;
            OutputBytes = outputBytes;
            Record = record;
            Repository = repository;
            Service = new DirectSourceReplacementService(repository);
        }

        public string Directory { get; }
        public string SourcePath { get; }
        public string OutputPath { get; }
        public string ReplacementPath { get; }
        public byte[] SourceBytes { get; }
        public byte[] OutputBytes { get; }
        public CompletedEncode Record { get; }
        public CompletedEncodeRepository Repository { get; }
        public DirectSourceReplacementService Service { get; }

        public static async Task<Fixture> CreateAsync(
            string sourceExtension,
            string outputExtension,
            bool outputAlreadyAtReplacementPath = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"hbcm-direct-{Guid.NewGuid():N}");
            var sourceDirectory = Path.Combine(directory, "source");
            var outputDirectory = Path.Combine(directory, "output");
            System.IO.Directory.CreateDirectory(sourceDirectory);
            System.IO.Directory.CreateDirectory(outputDirectory);
            var sourcePath = Path.Combine(sourceDirectory, $"video{sourceExtension}");
            var replacementPath = Path.Combine(sourceDirectory, $"video{outputExtension}");
            var outputPath = outputAlreadyAtReplacementPath
                ? replacementPath
                : Path.Combine(outputDirectory, $"encoded{outputExtension}");
            var sourceBytes = Enumerable.Range(0, 16_384).Select(i => (byte)(i % 251)).ToArray();
            var outputBytes = Enumerable.Range(0, 8_192).Select(i => (byte)(i % 239)).ToArray();
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            await File.WriteAllBytesAsync(outputPath, outputBytes);
            var now = DateTimeOffset.UtcNow;
            var record = new CompletedEncode(
                Guid.NewGuid(), $"DIRECT-{Guid.NewGuid():N}", now,
                sourcePath, Path.GetFileName(sourcePath), sourceExtension, sourceBytes.Length, true,
                outputPath, Path.GetFileName(outputPath), outputExtension, outputBytes.Length, true,
                File.GetLastWriteTimeUtc(outputPath), 50, 50, sourceBytes.Length - outputBytes.Length,
                0, "Completed", now, now);
            var repository = new CompletedEncodeRepository(Path.Combine(directory, "history.db"));
            await repository.InitializeAsync();
            Assert.True(await repository.AddAsync(record));
            return new Fixture(
                directory, sourcePath, outputPath, replacementPath,
                sourceBytes, outputBytes, record, repository);
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
