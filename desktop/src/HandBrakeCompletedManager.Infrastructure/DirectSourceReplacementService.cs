using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public enum DirectReplacementStage
{
    Preparing,
    Transferring,
    Installing,
    DeletingOutput,
    Completed
}

public sealed record DirectReplacementProgress(
    DirectReplacementStage Stage,
    string Message,
    long BytesTransferred,
    long TotalBytes,
    bool CanCancel)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp(BytesTransferred * 100d / TotalBytes, 0, 100);
}

public sealed record DirectReplacementResult(
    string ReplacementPath,
    bool OutputKept,
    bool UsedCopy);

public sealed class DirectSourceReplacementService(CompletedEncodeRepository repository)
{
    private const int BufferSize = 1024 * 1024;

    public async Task<DirectReplacementResult> ReplaceAsync(
        CompletedEncode record,
        bool keepOutput,
        IProgress<DirectReplacementProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var sourcePath = Path.GetFullPath(record.SourcePath);
        var outputPath = Path.GetFullPath(record.DestinationPath);
        var replacementPath = Path.GetFullPath(
            record.ReplacementPath ?? ReplacementPlanner.BuildPaths(record).FinalPath);
        var temporaryPath = $"{replacementPath}.{record.Id:N}.hbcm-direct";
        var outputSharesReplacementPath = PathsEqual(outputPath, replacementPath);

        if (PathsEqual(sourcePath, outputPath))
        {
            throw new InvalidOperationException("The source and output refer to the same file.");
        }

        if (record.FileActionStatus is "Source Replaced, Output Kept" &&
            !keepOutput &&
            !outputSharesReplacementPath &&
            File.Exists(replacementPath))
        {
            progress?.Report(new(DirectReplacementStage.DeletingOutput, "Deleting the retained output...", 0, 1, false));
            File.Delete(outputPath);
            await repository.UpsertFileActionAsync(
                record.Id, replacementPath, "Source Replaced", false, DateTimeOffset.UtcNow, CancellationToken.None);
            progress?.Report(new(DirectReplacementStage.Completed, "Source replaced and output deleted.", 1, 1, false));
            return new DirectReplacementResult(replacementPath, false, false);
        }

        progress?.Report(new(DirectReplacementStage.Preparing, "Checking the source and output paths...", 0, 1, false));
        var sourceExists = File.Exists(sourcePath);
        var outputExists = File.Exists(outputPath);
        var temporaryExists = File.Exists(temporaryPath);

        if (!sourceExists)
        {
            throw new FileNotFoundException($"The original source is missing. No files were removed. Review the output ({outputPath}), final path ({replacementPath}), and temporary path ({temporaryPath}) before proceeding.", sourcePath);
        }

        if (!outputExists)
        {
            throw new FileNotFoundException($"The encoded output is missing. No files were removed. Review the temporary path ({temporaryPath}) and final path ({replacementPath}) before proceeding.", outputPath);
        }

        if (temporaryExists)
        {
            throw new IOException($"An earlier temporary file needs review: {temporaryPath}. No files were removed or overwritten.");
        }

        if (outputSharesReplacementPath && !PathsEqual(sourcePath, replacementPath))
        {
            if (new FileInfo(outputPath).Length <= 0)
                throw new IOException($"The encoded output is empty: {outputPath}. The original source was not removed.");
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(DirectReplacementStage.Installing, "Removing the original source; the output is already in its final location...", 0, 1, false));
            File.Delete(sourcePath);
            var status = keepOutput ? "Source Replaced, Output Kept" : "Source Replaced";
            await repository.UpsertFileActionAsync(
                record.Id, replacementPath, status, keepOutput, DateTimeOffset.UtcNow, CancellationToken.None);
            progress?.Report(new(DirectReplacementStage.Completed, status, 1, 1, false));
            return new DirectReplacementResult(replacementPath, keepOutput, false);
        }

        if (!PathsEqual(sourcePath, replacementPath) &&
            File.Exists(replacementPath) &&
            !outputSharesReplacementPath)
        {
            throw new IOException($"The replacement path is already occupied: {replacementPath}");
        }

        var outputLength = new FileInfo(outputPath).Length;
        if (outputLength <= 0)
        {
            throw new IOException("The encoded output is empty.");
        }

        var sameVolume = string.Equals(
            Path.GetPathRoot(outputPath),
            Path.GetPathRoot(replacementPath),
            StringComparison.OrdinalIgnoreCase);
        var useCopy = keepOutput || !sameVolume;

        var sourceInfo = new FileInfo(sourcePath);
        var sourceLength = sourceInfo.Length;
        var sourceWriteTime = sourceInfo.LastWriteTimeUtc;
        var movedOutput = false;
        var installedDifferentExtension = false;
        try
        {
            if (useCopy)
            {
                await CopyWithProgressAsync(outputPath, temporaryPath, outputLength, progress, cancellationToken);
            }
            else
            {
                progress?.Report(new(DirectReplacementStage.Transferring, "Moving the output into position...", 0, outputLength, false));
                File.Move(outputPath, temporaryPath);
                movedOutput = true;
                progress?.Report(new(DirectReplacementStage.Transferring, "Output moved into position.", outputLength, outputLength, false));
            }

            if (new FileInfo(temporaryPath).Length != outputLength)
                throw new IOException("The transferred file size does not match the encoded output.");

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(DirectReplacementStage.Installing, "Replacing the original source...", outputLength, outputLength, false));
            sourceInfo.Refresh();
            if (!sourceInfo.Exists || sourceInfo.Length != sourceLength || sourceInfo.LastWriteTimeUtc != sourceWriteTime)
                throw new IOException("The source changed during transfer; replacement was stopped.");

            if (PathsEqual(sourcePath, replacementPath))
            {
                File.Replace(temporaryPath, sourcePath, null, ignoreMetadataErrors: true);
            }
            else
            {
                // Install without overwriting another file before removing the original.
                File.Move(temporaryPath, replacementPath);
                installedDifferentExtension = true;
                File.Delete(sourcePath);
            }
        }
        catch (Exception exception)
        {
            // If the output was moved, return our file to its old location when possible.
            // Never delete an ambiguous temporary or final file on a retry.
            var stagedPath = installedDifferentExtension ? replacementPath : temporaryPath;
            if (movedOutput && !File.Exists(outputPath) && File.Exists(stagedPath))
            {
                try { File.Move(stagedPath, outputPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (exception is OperationCanceledException && File.Exists(sourcePath) && File.Exists(outputPath) && !File.Exists(temporaryPath) && !installedDifferentExtension)
                throw;
            if (exception is OperationCanceledException)
                throw new IOException($"Replacement stopped. Check source ({sourcePath}), output ({outputPath}), final ({replacementPath}), and temporary file ({temporaryPath}) before retrying.", exception);
            throw new IOException($"Replacement stopped: {exception.Message} Check source ({sourcePath}), output ({outputPath}), final ({replacementPath}), and temporary file ({temporaryPath}) before retrying.", exception);
        }

        return await FinishRecordedReplacementAsync(
            record, replacementPath, outputPath, keepOutput, outputSharesReplacementPath, progress, useCopy);
    }

    private async Task<DirectReplacementResult> FinishRecordedReplacementAsync(
        CompletedEncode record,
        string replacementPath,
        string outputPath,
        bool keepOutput,
        bool outputSharesReplacementPath,
        IProgress<DirectReplacementProgress>? progress,
        bool usedCopy = false)
    {
        var outputKept = keepOutput || outputSharesReplacementPath;
        if (!outputKept && File.Exists(outputPath))
        {
            progress?.Report(new(DirectReplacementStage.DeletingOutput, "Deleting the output after replacement...", 0, 1, false));
            File.Delete(outputPath);
        }

        var status = outputKept ? "Source Replaced, Output Kept" : "Source Replaced";
        await repository.UpsertFileActionAsync(
            record.Id, replacementPath, status, outputKept, DateTimeOffset.UtcNow, CancellationToken.None);
        progress?.Report(new(DirectReplacementStage.Completed, status, 1, 1, false));
        return new DirectReplacementResult(replacementPath, outputKept, usedCopy);
    }

    private static async Task CopyWithProgressAsync(
        string sourcePath,
        string temporaryPath,
        long totalBytes,
        IProgress<DirectReplacementProgress>? progress,
        CancellationToken cancellationToken)
    {
        var created = false;
        try
        {
            await using var source = new FileStream(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            created = true;
            var buffer = new byte[BufferSize];
            long transferred = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                transferred += read;
                progress?.Report(new(
                    DirectReplacementStage.Transferring,
                    "Copying the encoded output...",
                    transferred,
                    totalBytes,
                    true));
            }

            await destination.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (created) TryDelete(temporaryPath);
            throw;
        }
        catch
        {
            if (created) TryDelete(temporaryPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
