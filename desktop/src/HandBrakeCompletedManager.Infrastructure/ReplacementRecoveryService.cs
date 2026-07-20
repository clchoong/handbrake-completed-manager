using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class ReplacementRecoveryService(
    ReplacementOperationRepository operationRepository,
    OriginalBackupRepository backupRepository,
    FinalizationTransactionRepository finalizationRepository)
{
    public async Task<IReadOnlyList<ReplacementRecoveryItem>> ReviewAsync(
        CancellationToken cancellationToken = default)
    {
        await operationRepository.InitializeAsync(cancellationToken);
        var operations = await operationRepository.GetRecoveryCandidatesAsync(cancellationToken);
        var items = new List<ReplacementRecoveryItem>();
        foreach (var operation in operations)
        {
            var temporaryExists = File.Exists(operation.TemporaryPath);
            var backup = await backupRepository.GetAsync(operation.Id, cancellationToken);
            var backupExists = backup is not null && File.Exists(backup.BackupPath);
            var finalization = await finalizationRepository.GetAsync(operation.Id, cancellationToken);
            if (finalization is not null)
            {
                items.Add(new ReplacementRecoveryItem(
                    operation.Id,
                    operation.CompletedEncodeId,
                    operation.SourcePath,
                    ReplacementRecoveryAction.ReviewFinalizationTransaction,
                    $"Finalisation checkpoint {finalization.Checkpoint} (revision {finalization.Revision}) requires record-specific recovery review.",
                    Max(Max(operation.DateUpdatedUtc, backup?.DateUpdatedUtc), finalization.DateUpdatedUtc)));
                continue;
            }

            var decision = ReplacementRecoveryClassifier.Review(operation, temporaryExists, backup, backupExists);
            if (!decision.ShouldDisplay)
            {
                continue;
            }

            items.Add(new ReplacementRecoveryItem(
                operation.Id,
                operation.CompletedEncodeId,
                operation.SourcePath,
                decision.Action,
                decision.Summary,
                Max(operation.DateUpdatedUtc, backup?.DateUpdatedUtc)));
        }

        return items;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset? right) =>
        right is not null && right.Value > left ? right.Value : left;

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        right > left ? right : left;
}
