using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class FinalizationPreparationService(FinalizationTransactionRepository repository)
{
    public async Task<FinalizationTransaction> PrepareAsync(
        ReplacementOperation operation,
        FinalizationReadinessResult readiness,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(readiness);
        if (!readiness.IsReady ||
            string.IsNullOrWhiteSpace(readiness.SourceSha256) ||
            string.IsNullOrWhiteSpace(readiness.TemporarySha256))
        {
            throw new InvalidOperationException("A finalisation transaction cannot be prepared without a successful integrity review.");
        }

        if (operation.Status != ReplacementOperationStatus.InProgress ||
            operation.Stage != ReplacementOperationStage.BackingUpSource ||
            operation.VerificationStatus != ReplacementVerificationStatus.Verified)
        {
            throw new InvalidOperationException("The replacement operation is not at the verified preparation boundary.");
        }

        await repository.InitializeAsync(cancellationToken);
        var existing = await repository.GetAsync(operation.Id, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.SourceSha256, readiness.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existing.FinalSha256, readiness.TemporarySha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The existing finalisation transaction was prepared from different file digests.");
            }

            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var transaction = new FinalizationTransaction(
            operation.Id,
            FinalizationCheckpoint.Prepared,
            readiness.SourceSha256,
            readiness.TemporarySha256,
            0,
            null,
            now,
            now);
        if (!await repository.TryCreatePreparedAsync(transaction, cancellationToken))
        {
            throw new InvalidOperationException("The persisted replacement and backup state changed before the transaction could be prepared.");
        }

        return transaction;
    }
}
