using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed record OriginalBackupCleanupResult(
    Guid OperationId,
    string BackupPath,
    long BytesRemoved);

public sealed class OriginalBackupCleanupService(
    ReplacementOperationRepository operationRepository,
    OriginalBackupRepository backupRepository,
    Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<OriginalBackupCleanupResult> DiscardAsync(
        ReplacementPlan plan,
        ReplacementOperation operation,
        OriginalBackupState backup,
        CancellationToken cancellationToken = default)
    {
        Validate(plan, operation, backup);
        if (Directory.Exists(backup.BackupPath))
        {
            throw new InvalidOperationException("The recorded original-backup path is a directory.");
        }

        if (!File.Exists(backup.BackupPath))
        {
            throw new FileNotFoundException("The recorded original-backup file no longer exists.", backup.BackupPath);
        }

        var latestOperation = await operationRepository.GetLatestForCompletedEncodeAsync(
            plan.CompletedEncode.Id,
            cancellationToken);
        var latestBackup = await backupRepository.GetAsync(operation.Id, cancellationToken);
        if (latestOperation is null || latestOperation.Id != operation.Id ||
            latestBackup is null || latestBackup.DateUpdatedUtc != backup.DateUpdatedUtc)
        {
            throw new InvalidOperationException("Backup state changed after review. Refresh before cleanup.");
        }

        var bytesRemoved = new FileInfo(backup.BackupPath).Length;
        await using var exclusiveStream = new FileStream(
            backup.BackupPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Delete,
            bufferSize: 1,
            FileOptions.None);
        if (!await backupRepository.TryCancelForCleanupAsync(
                operation.Id,
                backup.DateUpdatedUtc,
                _clock(),
                cancellationToken))
        {
            throw new InvalidOperationException("Backup state changed while cleanup was starting.");
        }

        File.Delete(backup.BackupPath);
        if (File.Exists(backup.BackupPath))
        {
            throw new IOException("The original-backup artifact could not be removed.");
        }

        await operationRepository.TryReturnToVerifiedTemporaryAsync(
            operation.Id,
            _clock(),
            CancellationToken.None);
        return new OriginalBackupCleanupResult(operation.Id, backup.BackupPath, bytesRemoved);
    }

    private static void Validate(
        ReplacementPlan plan,
        ReplacementOperation operation,
        OriginalBackupState backup)
    {
        if (operation.CompletedEncodeId != plan.CompletedEncode.Id ||
            backup.OperationId != operation.Id ||
            !PathsEqual(backup.BackupPath, plan.Paths.BackupPath) ||
            !PathsEqual(operation.BackupPath, plan.Paths.BackupPath) ||
            PathsEqual(backup.BackupPath, operation.SourcePath) ||
            PathsEqual(backup.BackupPath, operation.DestinationPath) ||
            PathsEqual(backup.BackupPath, operation.FinalPath) ||
            PathsEqual(backup.BackupPath, operation.TemporaryPath))
        {
            throw new InvalidOperationException(
                "The original-backup artifact does not exactly match the reviewed operation.");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
