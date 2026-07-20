using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class FinalizationReadinessService
{
    public async Task<FinalizationReadinessResult> ReviewAsync(
        ReplacementPlan plan,
        ReplacementOperation operation,
        OriginalBackupState backup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(backup);
        var issues = new List<FinalizationReadinessIssue>();

        AddWhen(operation.CompletedEncodeId != plan.CompletedEncode.Id, "OperationMismatch", "The operation does not belong to the reviewed encode.", issues);
        AddWhen(backup.OperationId != operation.Id, "BackupStateMismatch", "The backup state does not belong to the reviewed operation.", issues);
        AddWhen(!PathsEqual(operation.SourcePath, plan.CompletedEncode.SourcePath) ||
                !PathsEqual(operation.DestinationPath, plan.CompletedEncode.DestinationPath) ||
                !PathsEqual(operation.FinalPath, plan.Paths.FinalPath) ||
                !PathsEqual(operation.TemporaryPath, plan.Paths.TemporaryPath) ||
                !PathsEqual(operation.BackupPath, plan.Paths.BackupPath) ||
                !PathsEqual(backup.BackupPath, plan.Paths.BackupPath),
            "PathMismatch", "Recorded paths do not exactly match the current reviewed plan.", issues);
        AddWhen(operation.Status != ReplacementOperationStatus.InProgress,
            "OperationNotActive", "The replacement operation is not in progress.", issues);
        AddWhen(operation.Stage != ReplacementOperationStage.BackingUpSource,
            "BackupStageIncomplete", "The operation has not reached the verified original-backup stage.", issues);
        AddWhen(operation.VerificationStatus != ReplacementVerificationStatus.Verified,
            "TemporaryNotVerified", "The converted temporary copy is not marked verified.", issues);
        AddWhen(backup.Status != OriginalBackupStatus.Verified || string.IsNullOrWhiteSpace(backup.Sha256),
            "BackupNotVerified", "The original-backup copy is not marked verified with a SHA-256 digest.", issues);
        AddWhen(PathExists(operation.FinalPath),
            "FinalPathConflict", "A file or directory already occupies the planned final path.", issues);

        var source = ReadFile(operation.SourcePath);
        var destination = ReadFile(operation.DestinationPath);
        var temporary = ReadFile(operation.TemporaryPath);
        var backupFile = ReadFile(operation.BackupPath);
        AddWhen(!source.Exists || source.Size != operation.SourceSize,
            "SourceChanged", "The source is missing or no longer has its recorded size.", issues);
        AddWhen(!destination.Exists || destination.Size != operation.DestinationSize,
            "DestinationChanged", "The converted output is missing or no longer has its recorded size.", issues);
        AddWhen(!temporary.Exists || temporary.Size != operation.DestinationSize,
            "TemporaryChanged", "The temporary converted copy is missing or has an unexpected size.", issues);
        AddWhen(!backupFile.Exists || backupFile.Size != operation.SourceSize,
            "BackupChanged", "The original-backup copy is missing or has an unexpected size.", issues);
        AddWhen(operation.BytesCopied != operation.DestinationSize,
            "TemporaryProgressIncomplete", "Persisted temporary-copy progress is incomplete.", issues);
        AddWhen(backup.BytesCopied != operation.SourceSize,
            "BackupProgressIncomplete", "Persisted original-backup progress is incomplete.", issues);

        if (issues.Count > 0)
        {
            return new FinalizationReadinessResult(false, null, null, issues);
        }

        string destinationHash;
        string temporaryHash;
        string sourceHash;
        string backupHash;
        try
        {
            var before = CaptureSnapshots(operation);
            destinationHash = await HashStableFileAsync(operation.DestinationPath, cancellationToken);
            temporaryHash = await HashStableFileAsync(operation.TemporaryPath, cancellationToken);
            sourceHash = await HashStableFileAsync(operation.SourcePath, cancellationToken);
            backupHash = await HashStableFileAsync(operation.BackupPath, cancellationToken);
            EnsureSnapshotsUnchanged(before, operation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(new FinalizationReadinessIssue(
                "FileReadFailed",
                $"A readiness file could not be read safely: {exception.Message}"));
            return new FinalizationReadinessResult(false, null, null, issues);
        }

        AddWhen(!string.Equals(destinationHash, temporaryHash, StringComparison.Ordinal),
            "TemporaryHashMismatch", "The temporary converted copy no longer matches the converted output.", issues);
        AddWhen(!string.Equals(sourceHash, backupHash, StringComparison.Ordinal),
            "BackupHashMismatch", "The original-backup copy no longer matches the source.", issues);
        AddWhen(!string.Equals(sourceHash, backup.Sha256, StringComparison.OrdinalIgnoreCase),
            "PersistedBackupHashMismatch", "The current source and backup do not match the persisted verified backup digest.", issues);
        return new FinalizationReadinessResult(
            issues.Count == 0,
            temporaryHash,
            sourceHash,
            issues);
    }

    private static async Task<string> HashStableFileAsync(string path, CancellationToken cancellationToken)
    {
        var initialInfo = new FileInfo(path);
        var initialSize = initialInfo.Length;
        var initialLastWriteUtc = initialInfo.LastWriteTimeUtc;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        var finalInfo = new FileInfo(path);
        if (stream.Length != initialSize ||
            finalInfo.Length != initialSize ||
            finalInfo.LastWriteTimeUtc != initialLastWriteUtc)
        {
            throw new IOException($"The file changed while it was being checked: {path}");
        }

        return hash;
    }

    private static IReadOnlyDictionary<string, FileSnapshot> CaptureSnapshots(ReplacementOperation operation) =>
        new[] { operation.SourcePath, operation.DestinationPath, operation.TemporaryPath, operation.BackupPath }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, CaptureSnapshot, StringComparer.OrdinalIgnoreCase);

    private static void EnsureSnapshotsUnchanged(
        IReadOnlyDictionary<string, FileSnapshot> before,
        ReplacementOperation operation)
    {
        foreach (var path in new[] { operation.SourcePath, operation.DestinationPath, operation.TemporaryPath, operation.BackupPath }
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (before[path] != CaptureSnapshot(path))
            {
                throw new IOException($"The file changed during the readiness review: {path}");
            }
        }
    }

    private static FileSnapshot CaptureSnapshot(string path)
    {
        var info = new FileInfo(path);
        return new FileSnapshot(info.Length, info.LastWriteTimeUtc);
    }

    private sealed record FileSnapshot(long Length, DateTime LastWriteTimeUtc);

    private static (bool Exists, long? Size) ReadFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? (true, file.Length) : (false, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (File.Exists(path), null);
        }
    }

    private static void AddWhen(
        bool condition,
        string code,
        string message,
        ICollection<FinalizationReadinessIssue> issues)
    {
        if (condition)
        {
            issues.Add(new FinalizationReadinessIssue(code, message));
        }
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
