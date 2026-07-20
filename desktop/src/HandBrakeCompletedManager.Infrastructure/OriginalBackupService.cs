using System.Buffers;
using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class InsufficientBackupSpaceException(long requiredBytes, long availableBytes)
    : IOException($"The original backup requires {requiredBytes} bytes, but only {availableBytes} bytes are available.")
{
    public long RequiredBytes { get; } = requiredBytes;
    public long AvailableBytes { get; } = availableBytes;
}

public sealed class OriginalBackupVerificationException(string message) : IOException(message);

public sealed class OriginalBackupService(
    ReplacementOperationRepository operationRepository,
    OriginalBackupRepository backupRepository,
    IAvailableSpaceProvider? availableSpaceProvider = null,
    Func<DateTimeOffset>? clock = null)
{
    private const int BufferSize = 1024 * 1024;
    private const long ProgressPersistenceInterval = 4L * 1024 * 1024;
    private readonly IAvailableSpaceProvider _availableSpaceProvider =
        availableSpaceProvider ?? new DriveAvailableSpaceProvider();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<OriginalBackupResult> CopyAndVerifyAsync(
        ReplacementPlan plan,
        ReplacementOperation operation,
        IProgress<OriginalBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operation);
        ValidateOperation(plan, operation);
        await operationRepository.InitializeAsync(CancellationToken.None);
        await backupRepository.InitializeAsync(CancellationToken.None);
        var latest = await operationRepository.GetLatestForCompletedEncodeAsync(
            plan.CompletedEncode.Id,
            cancellationToken);
        if (latest is null || latest.Id != operation.Id || latest.DateUpdatedUtc != operation.DateUpdatedUtc)
        {
            throw new InvalidOperationException(
                "The replacement operation changed after review. Refresh before creating the original backup.");
        }

        ValidateCurrentFiles(plan, latest);
        var now = _clock();
        var backup = new OriginalBackupState(
            latest.Id,
            latest.BackupPath,
            OriginalBackupStatus.Planned,
            latest.SourceSize,
            0,
            null,
            null,
            now,
            now);
        var backupBegan = false;
        var bytesCopied = 0L;

        try
        {
            if (!await backupRepository.TryBeginAsync(
                    backup,
                    latest.DateUpdatedUtc,
                    CancellationToken.None))
            {
                throw new InvalidOperationException(
                    "The verified temporary-copy state changed before backup could start.");
            }

            backupBegan = true;
            cancellationToken.ThrowIfCancellationRequested();
            var availableBytes = _availableSpaceProvider.GetAvailableBytes(latest.BackupPath);
            if (availableBytes < latest.SourceSize)
            {
                throw new InsufficientBackupSpaceException(latest.SourceSize, availableBytes);
            }

            await RequireBackupUpdateAsync(
                latest.Id,
                OriginalBackupStatus.Copying,
                0,
                null,
                null);
            cancellationToken.ThrowIfCancellationRequested();

            var backupDirectory = Path.GetDirectoryName(latest.BackupPath)
                ?? throw new InvalidOperationException("The original-backup path has no parent directory.");
            if (File.Exists(backupDirectory))
            {
                throw new IOException("A file occupies the planned original-backup directory path.");
            }

            Directory.CreateDirectory(backupDirectory);
            await using var sourceStream = new FileStream(
                latest.SourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (sourceStream.Length != latest.SourceSize)
            {
                throw new OriginalBackupVerificationException(
                    "The source file size changed before original backup began.");
            }

            var initialLastWriteUtc = File.GetLastWriteTimeUtc(latest.SourcePath);
            await using var backupStream = new FileStream(
                latest.BackupPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                var nextPersistence = ProgressPersistenceInterval;
                while (true)
                {
                    var bytesRead = await sourceStream.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await backupStream.WriteAsync(
                        buffer.AsMemory(0, bytesRead),
                        cancellationToken);
                    bytesCopied += bytesRead;
                    progress?.Report(new OriginalBackupProgress(
                        latest.Id,
                        bytesCopied,
                        latest.SourceSize));
                    if (bytesCopied >= nextPersistence)
                    {
                        await RequireBackupUpdateAsync(
                            latest.Id,
                            OriginalBackupStatus.Copying,
                            bytesCopied,
                            null,
                            null);
                        nextPersistence = bytesCopied + ProgressPersistenceInterval;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await backupStream.FlushAsync(cancellationToken);
            backupStream.Flush(flushToDisk: true);
            await RequireBackupUpdateAsync(
                latest.Id,
                OriginalBackupStatus.Verifying,
                bytesCopied,
                null,
                null);
            if (bytesCopied != latest.SourceSize || backupStream.Length != latest.SourceSize)
            {
                throw new OriginalBackupVerificationException(
                    "The original-backup size does not match the source file.");
            }

            sourceStream.Position = 0;
            backupStream.Position = 0;
            var sourceHash = await SHA256.HashDataAsync(sourceStream, cancellationToken);
            var backupHash = await SHA256.HashDataAsync(backupStream, cancellationToken);
            if (!sourceHash.AsSpan().SequenceEqual(backupHash))
            {
                throw new OriginalBackupVerificationException(
                    "The original-backup SHA-256 does not match the source file.");
            }

            if (sourceStream.Length != latest.SourceSize ||
                File.GetLastWriteTimeUtc(latest.SourcePath) != initialLastWriteUtc)
            {
                throw new OriginalBackupVerificationException(
                    "The source file changed while its backup was being copied or verified.");
            }

            var sha256 = Convert.ToHexString(backupHash);
            await RequireBackupUpdateAsync(
                latest.Id,
                OriginalBackupStatus.Verified,
                bytesCopied,
                sha256,
                null);
            return new OriginalBackupResult(latest.Id, latest.BackupPath, bytesCopied, sha256);
        }
        catch (OperationCanceledException)
        {
            if (backupBegan)
            {
                await TryBackupUpdateAsync(
                    latest.Id,
                    OriginalBackupStatus.Cancelled,
                    bytesCopied,
                    "Original backup cancelled; any partial backup requires review.");
            }

            await TryReturnOperationAsync(latest.Id);
            throw;
        }
        catch (Exception exception)
        {
            if (backupBegan)
            {
                await TryBackupUpdateAsync(
                    latest.Id,
                    OriginalBackupStatus.Failed,
                    bytesCopied,
                    exception.Message);
            }

            await TryReturnOperationAsync(latest.Id);
            throw;
        }
    }

    private static void ValidateOperation(ReplacementPlan plan, ReplacementOperation operation)
    {
        if (operation.CompletedEncodeId != plan.CompletedEncode.Id ||
            !PathsEqual(operation.SourcePath, plan.CompletedEncode.SourcePath) ||
            !PathsEqual(operation.DestinationPath, plan.CompletedEncode.DestinationPath) ||
            !PathsEqual(operation.FinalPath, plan.Paths.FinalPath) ||
            !PathsEqual(operation.TemporaryPath, plan.Paths.TemporaryPath) ||
            !PathsEqual(operation.BackupPath, plan.Paths.BackupPath) ||
            operation.Status != ReplacementOperationStatus.InProgress ||
            operation.Stage != ReplacementOperationStage.Verifying ||
            operation.VerificationStatus != ReplacementVerificationStatus.Verified)
        {
            throw new InvalidOperationException(
                "Original backup requires the latest verified temporary-copy operation and matching reviewed paths.");
        }
    }

    private static void ValidateCurrentFiles(ReplacementPlan plan, ReplacementOperation operation)
    {
        if (!File.Exists(operation.SourcePath) ||
            new FileInfo(operation.SourcePath).Length != operation.SourceSize ||
            !File.Exists(operation.TemporaryPath) ||
            new FileInfo(operation.TemporaryPath).Length != operation.DestinationSize ||
            File.Exists(operation.FinalPath) ||
            Directory.Exists(operation.FinalPath) ||
            File.Exists(operation.BackupPath) ||
            Directory.Exists(operation.BackupPath) ||
            (plan.CompletedEncode.SourceSize is not null &&
             plan.CompletedEncode.SourceSize != operation.SourceSize))
        {
            throw new InvalidOperationException(
                "Current files no longer match the verified backup plan or the backup path is occupied.");
        }
    }

    private async Task RequireBackupUpdateAsync(
        Guid operationId,
        OriginalBackupStatus status,
        long bytesCopied,
        string? sha256,
        string? failureMessage)
    {
        if (!await backupRepository.UpdateAsync(
                operationId,
                status,
                bytesCopied,
                sha256,
                failureMessage,
                _clock(),
                CancellationToken.None))
        {
            throw new InvalidOperationException("The original-backup state no longer exists.");
        }
    }

    private async Task TryBackupUpdateAsync(
        Guid operationId,
        OriginalBackupStatus status,
        long bytesCopied,
        string failureMessage)
    {
        try
        {
            await backupRepository.UpdateAsync(
                operationId,
                status,
                bytesCopied,
                null,
                failureMessage,
                _clock(),
                CancellationToken.None);
        }
        catch (Exception)
        {
            // Preserve the original backup exception.
        }
    }

    private async Task TryReturnOperationAsync(Guid operationId)
    {
        try
        {
            await operationRepository.TryReturnToVerifiedTemporaryAsync(
                operationId,
                _clock(),
                CancellationToken.None);
        }
        catch (Exception)
        {
            // Preserve the original backup exception.
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
