using System.Security.Cryptography;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class FinalizationRecoveryAssessmentService
{
    public async Task<FinalizationRecoveryDecision> ReviewAsync(
        ReplacementOperation operation,
        FinalizationTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(transaction);
        if (operation.Id != transaction.OperationId)
        {
            return Manual("The finalisation journal does not belong to the replacement operation.");
        }

        try
        {
            var replacesSourceInPlace = PathsEqual(operation.SourcePath, operation.FinalPath);
            var source = await ObserveAsync(operation.SourcePath, cancellationToken);
            var final = replacesSourceInPlace
                ? source
                : await ObserveAsync(operation.FinalPath, cancellationToken);
            if (replacesSourceInPlace)
            {
                if (Matches(source, transaction.SourceSha256))
                {
                    final = new FinalizationArtifactObservation(false);
                }
                else if (Matches(source, transaction.FinalSha256))
                {
                    source = new FinalizationArtifactObservation(false);
                }
            }

            var artifacts = new FinalizationArtifactSnapshot(
                source,
                await ObserveAsync(operation.TemporaryPath, cancellationToken),
                final,
                await ObserveAsync(operation.BackupPath, cancellationToken));
            return FinalizationRecoveryAdvisor.Review(transaction, artifacts, replacesSourceInPlace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Manual($"The transaction artifacts could not be inspected safely: {exception.Message}");
        }
    }

    private static async Task<FinalizationArtifactObservation> ObserveAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(path))
        {
            throw new IOException($"A directory occupies the expected file path: {path}");
        }

        if (!File.Exists(path))
        {
            return new FinalizationArtifactObservation(false);
        }

        var initial = new FileInfo(path);
        var initialLength = initial.Length;
        var initialWriteUtc = initial.LastWriteTimeUtc;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        var final = new FileInfo(path);
        if (stream.Length != initialLength ||
            final.Length != initialLength ||
            final.LastWriteTimeUtc != initialWriteUtc)
        {
            throw new IOException($"A transaction artifact changed while it was being inspected: {path}");
        }

        return new FinalizationArtifactObservation(true, hash);
    }

    private static FinalizationRecoveryDecision Manual(string message) =>
        new(false, FinalizationRecoveryAction.ManualReview, FinalizationCheckpoint.RecoveryRequired, message);

    private static bool Matches(FinalizationArtifactObservation artifact, string sha256) =>
        artifact.Exists && string.Equals(artifact.Sha256, sha256, StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
