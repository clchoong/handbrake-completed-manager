using System.Buffers;
using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public interface IAvailableSpaceProvider
{
    long GetAvailableBytes(string path);
}

public sealed class DriveAvailableSpaceProvider : IAvailableSpaceProvider
{
    public long GetAvailableBytes(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))
            ?? throw new IOException("The temporary-copy path has no drive root.");
        return new DriveInfo(root).AvailableFreeSpace;
    }
}

public sealed class InsufficientDiskSpaceException(long requiredBytes, long availableBytes)
    : IOException($"The temporary copy requires {requiredBytes} bytes, but only {availableBytes} bytes are available.")
{
    public long RequiredBytes { get; } = requiredBytes;
    public long AvailableBytes { get; } = availableBytes;
}

public sealed class TemporaryCopyVerificationException(string message) : IOException(message);

public sealed class TemporaryCopyService(
    ReplacementOperationRepository operationRepository,
    IAvailableSpaceProvider? availableSpaceProvider = null,
    Func<DateTimeOffset>? clock = null)
{
    private const int BufferSize = 1024 * 1024;
    private const long ProgressPersistenceInterval = 4L * 1024 * 1024;

    private readonly IAvailableSpaceProvider _availableSpaceProvider =
        availableSpaceProvider ?? new DriveAvailableSpaceProvider();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<TemporaryCopyResult> CopyAndVerifyAsync(
        ReplacementPlan plan,
        IProgress<ReplacementCopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var currentPlan = new ReplacementPreflightService().Review(plan.CompletedEncode);
        if (!currentPlan.CanProceed)
        {
            throw new InvalidOperationException("Replacement preflight no longer passes. Run the safety review again.");
        }

        var operation = ReplacementOperationFactory.CreatePlanned(currentPlan, _clock());
        var operationAdded = false;
        var bytesCopied = 0L;

        try
        {
            await operationRepository.InitializeAsync(CancellationToken.None);
            await operationRepository.AddAsync(operation, CancellationToken.None);
            operationAdded = true;
            cancellationToken.ThrowIfCancellationRequested();

            var availableBytes = _availableSpaceProvider.GetAvailableBytes(operation.TemporaryPath);
            if (availableBytes < operation.DestinationSize)
            {
                throw new InsufficientDiskSpaceException(operation.DestinationSize, availableBytes);
            }

            await RequireStateUpdateAsync(
                operation.Id,
                ReplacementOperationStatus.InProgress,
                ReplacementOperationStage.Copying,
                0,
                ReplacementVerificationStatus.NotVerified,
                null);

            await using var sourceStream = new FileStream(
                operation.DestinationPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (sourceStream.Length != operation.DestinationSize)
            {
                throw new TemporaryCopyVerificationException("The converted file size changed before copying began.");
            }

            var initialLastWriteUtc = File.GetLastWriteTimeUtc(operation.DestinationPath);
            await using var temporaryStream = new FileStream(
                operation.TemporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                var nextProgressPersistence = ProgressPersistenceInterval;
                while (true)
                {
                    var bytesRead = await sourceStream.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await temporaryStream.WriteAsync(
                        buffer.AsMemory(0, bytesRead),
                        cancellationToken);
                    bytesCopied += bytesRead;
                    progress?.Report(new ReplacementCopyProgress(
                        operation.Id,
                        bytesCopied,
                        operation.DestinationSize));

                    if (bytesCopied >= nextProgressPersistence)
                    {
                        await RequireStateUpdateAsync(
                            operation.Id,
                            ReplacementOperationStatus.InProgress,
                            ReplacementOperationStage.Copying,
                            bytesCopied,
                            ReplacementVerificationStatus.NotVerified,
                            null);
                        nextProgressPersistence = bytesCopied + ProgressPersistenceInterval;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await temporaryStream.FlushAsync(cancellationToken);
            temporaryStream.Flush(flushToDisk: true);
            await RequireStateUpdateAsync(
                operation.Id,
                ReplacementOperationStatus.InProgress,
                ReplacementOperationStage.Verifying,
                bytesCopied,
                ReplacementVerificationStatus.NotVerified,
                null);

            if (bytesCopied != operation.DestinationSize || temporaryStream.Length != operation.DestinationSize)
            {
                throw new TemporaryCopyVerificationException("The temporary-copy size does not match the converted file.");
            }

            sourceStream.Position = 0;
            temporaryStream.Position = 0;
            var sourceHash = await SHA256.HashDataAsync(sourceStream, cancellationToken);
            var temporaryHash = await SHA256.HashDataAsync(temporaryStream, cancellationToken);
            if (!sourceHash.AsSpan().SequenceEqual(temporaryHash))
            {
                throw new TemporaryCopyVerificationException("The temporary-copy SHA-256 does not match the converted file.");
            }

            if (sourceStream.Length != operation.DestinationSize ||
                File.GetLastWriteTimeUtc(operation.DestinationPath) != initialLastWriteUtc)
            {
                throw new TemporaryCopyVerificationException("The converted file changed while it was being copied or verified.");
            }

            await RequireStateUpdateAsync(
                operation.Id,
                ReplacementOperationStatus.InProgress,
                ReplacementOperationStage.Verifying,
                bytesCopied,
                ReplacementVerificationStatus.Verified,
                null);
            return new TemporaryCopyResult(
                operation.Id,
                operation.TemporaryPath,
                bytesCopied,
                Convert.ToHexString(temporaryHash));
        }
        catch (OperationCanceledException)
        {
            if (operationAdded)
            {
                await TryUpdateTerminalStateAsync(
                    operation.Id,
                    ReplacementOperationStatus.Cancelled,
                    ReplacementOperationStage.Cancelled,
                    bytesCopied,
                    ReplacementVerificationStatus.NotVerified,
                    "Temporary copy cancelled; any partial temporary file requires review.");
            }

            throw;
        }
        catch (Exception exception)
        {
            if (operationAdded)
            {
                await TryUpdateTerminalStateAsync(
                    operation.Id,
                    ReplacementOperationStatus.Failed,
                    ReplacementOperationStage.Failed,
                    bytesCopied,
                    ReplacementVerificationStatus.Failed,
                    exception.Message);
            }

            throw;
        }
    }

    private async Task RequireStateUpdateAsync(
        Guid operationId,
        ReplacementOperationStatus status,
        ReplacementOperationStage stage,
        long bytesCopied,
        ReplacementVerificationStatus verificationStatus,
        string? failureMessage)
    {
        var updated = await operationRepository.UpdateStateAsync(
            operationId,
            status,
            stage,
            bytesCopied,
            verificationStatus,
            failureMessage,
            _clock(),
            CancellationToken.None);
        if (!updated)
        {
            throw new InvalidOperationException("The replacement operation state no longer exists.");
        }
    }

    private async Task TryUpdateTerminalStateAsync(
        Guid operationId,
        ReplacementOperationStatus status,
        ReplacementOperationStage stage,
        long bytesCopied,
        ReplacementVerificationStatus verificationStatus,
        string failureMessage)
    {
        try
        {
            await operationRepository.UpdateStateAsync(
                operationId,
                status,
                stage,
                bytesCopied,
                verificationStatus,
                failureMessage,
                _clock(),
                CancellationToken.None);
        }
        catch (Exception)
        {
            // Terminal-state persistence is best effort here so it cannot hide the
            // original copy, cancellation, or verification exception.
        }
    }
}
