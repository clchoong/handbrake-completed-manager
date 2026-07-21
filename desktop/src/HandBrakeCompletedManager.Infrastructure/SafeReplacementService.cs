using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class SafeReplacementService(
    ReplacementOperationRepository operationRepository,
    OriginalBackupRepository backupRepository,
    FinalizationTransactionRepository transactionRepository,
    TemporaryCopyService temporaryCopyService,
    OriginalBackupService originalBackupService,
    FinalizationReadinessService readinessService,
    FinalizationPreparationService preparationService,
    FinalizationPromotionService promotionService,
    SourceRecycleService sourceRecycleService,
    FinalizationCompletionService completionService)
{
    public async Task<SafeReplacementResult> ReplaceAsync(
        ReplacementPlan reviewedPlan,
        IProgress<SafeReplacementProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewedPlan);
        var plan = new ReplacementPreflightService().Review(reviewedPlan.CompletedEncode);
        if (!plan.CanProceed)
        {
            throw new InvalidOperationException(
                "Replacement safety checks no longer pass. Review the displayed paths and issues again.");
        }

        await operationRepository.InitializeAsync(cancellationToken);
        await backupRepository.InitializeAsync(cancellationToken);
        await transactionRepository.InitializeAsync(cancellationToken);
        await EnsureNoBlockingRecoveryAsync(plan.CompletedEncode.Id, cancellationToken);

        progress?.Report(new SafeReplacementProgress(
            SafeReplacementStage.CopyingConvertedFile,
            "Copying and SHA-256 verifying the converted file...",
            0));
        var copyProgress = new ForwardProgress<ReplacementCopyProgress>(value =>
            progress?.Report(new SafeReplacementProgress(
                SafeReplacementStage.CopyingConvertedFile,
                "Copying and SHA-256 verifying the converted file...",
                value.Percentage)));
        var copy = await temporaryCopyService.CopyAndVerifyAsync(plan, copyProgress, cancellationToken);
        var operation = await RequireOperationAsync(copy.OperationId, cancellationToken);

        progress?.Report(new SafeReplacementProgress(
            SafeReplacementStage.BackingUpOriginalSource,
            "Creating and SHA-256 verifying the original-source backup...",
            0));
        var backupProgress = new ForwardProgress<OriginalBackupProgress>(value =>
            progress?.Report(new SafeReplacementProgress(
                SafeReplacementStage.BackingUpOriginalSource,
                "Creating and SHA-256 verifying the original-source backup...",
                value.Percentage)));
        await originalBackupService.CopyAndVerifyAsync(plan, operation, backupProgress, cancellationToken);
        operation = await RequireOperationAsync(copy.OperationId, cancellationToken);
        var backup = await backupRepository.GetAsync(copy.OperationId, cancellationToken)
            ?? throw new InvalidOperationException("The verified original-backup state could not be reloaded.");

        progress?.Report(new SafeReplacementProgress(
            SafeReplacementStage.VerifyingAllArtifacts,
            "Rechecking every path, size, file state, and SHA-256 digest..."));
        var readiness = await readinessService.ReviewAsync(plan, operation, backup, cancellationToken);
        if (!readiness.IsReady)
        {
            throw new InvalidOperationException(
                "Final replacement verification was blocked: " +
                string.Join(" ", readiness.Issues.Select(issue => issue.Message)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await preparationService.PrepareAsync(operation, readiness, CancellationToken.None);

        // Once the durable finalisation journal exists, continue through its guarded
        // checkpoints. A process interruption remains recoverable from the journal.
        progress?.Report(new SafeReplacementProgress(
            SafeReplacementStage.PromotingConvertedFile,
            "Promoting the verified converted copy without overwriting any file..."));
        await promotionService.PromoteAsync(operation.Id, CancellationToken.None);

        progress?.Report(new SafeReplacementProgress(
            SafeReplacementStage.RecyclingOriginalSource,
            PathsEqual(operation.SourcePath, operation.FinalPath)
                ? "Revalidating the atomic replacement and verified original backup..."
                : "Revalidating all artifacts and moving the original source to the Windows Recycle Bin..."));
        await sourceRecycleService.RecycleAsync(operation.Id, CancellationToken.None);

        progress?.Report(new SafeReplacementProgress(
            SafeReplacementStage.CompletingTransaction,
            "Completing the replacement transaction atomically..."));
        var completion = await completionService.CompleteAsync(operation.Id, CancellationToken.None);
        return new SafeReplacementResult(operation.Id, completion.FinalPath, operation.BackupPath);
    }

    private async Task EnsureNoBlockingRecoveryAsync(Guid completedEncodeId, CancellationToken cancellationToken)
    {
        var existing = await operationRepository.GetLatestForCompletedEncodeAsync(
            completedEncodeId,
            cancellationToken);
        if (existing is null)
        {
            return;
        }

        var recovery = ReplacementRecoveryAdvisor.Review(
            existing,
            File.Exists(existing.TemporaryPath));
        var transaction = await transactionRepository.GetAsync(existing.Id, cancellationToken);
        if (recovery.BlocksNewCopy ||
            transaction?.Checkpoint is not (null or FinalizationCheckpoint.Undone))
        {
            throw new InvalidOperationException(
                "Existing replacement work requires recovery review before a new one-click replacement can start.");
        }
    }

    private async Task<ReplacementOperation> RequireOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken) =>
        await operationRepository.GetByIdAsync(operationId, cancellationToken)
        ?? throw new InvalidOperationException("The replacement operation state could not be reloaded.");

    private sealed class ForwardProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
