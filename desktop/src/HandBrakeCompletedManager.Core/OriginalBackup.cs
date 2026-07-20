namespace HandBrakeCompletedManager.Core;

public enum OriginalBackupStatus
{
    Planned,
    Copying,
    Verifying,
    Verified,
    Failed,
    Cancelled
}

public sealed record OriginalBackupState(
    Guid OperationId,
    string BackupPath,
    OriginalBackupStatus Status,
    long SourceSize,
    long BytesCopied,
    string? Sha256,
    string? FailureMessage,
    DateTimeOffset DateCreatedUtc,
    DateTimeOffset DateUpdatedUtc);

public sealed record OriginalBackupProgress(
    Guid OperationId,
    long BytesCopied,
    long TotalBytes)
{
    public double Percentage => TotalBytes <= 0 ? 0 : BytesCopied * 100d / TotalBytes;
}

public sealed record OriginalBackupResult(
    Guid OperationId,
    string BackupPath,
    long BytesCopied,
    string Sha256);
