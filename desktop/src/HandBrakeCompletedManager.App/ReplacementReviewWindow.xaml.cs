using System.ComponentModel;
using System.IO;
using System.Windows;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.App;

public partial class ReplacementReviewWindow : Window
{
    private ReplacementPlan _plan;
    private readonly TemporaryCopyService _temporaryCopyService;
    private readonly TemporaryCopyCleanupService _temporaryCopyCleanupService;
    private readonly ReplacementOperationRepository _operationRepository;
    private readonly OriginalBackupRepository _backupRepository;
    private readonly OriginalBackupService _originalBackupService;
    private readonly OriginalBackupCleanupService _originalBackupCleanupService;
    private readonly FinalizationReadinessService _finalizationReadinessService;
    private readonly FinalizationTransactionRepository _finalizationTransactionRepository;
    private readonly FinalizationPreparationService _finalizationPreparationService;
    private readonly FinalizationPromotionService _finalizationPromotionService;
    private readonly SourceRecycleService _sourceRecycleService;
    private readonly FinalizationCompletionService _finalizationCompletionService;
    private readonly SafeReplacementService _safeReplacementService;
    private readonly UndoPreparationService _undoPreparationService;
    private readonly SourceRestorationService _sourceRestorationService;
    private readonly FinalFileRecycleService _finalFileRecycleService;
    private readonly UndoCompletionService _undoCompletionService;
    private readonly ReplacementPreflightService _preflightService = new();
    private ReplacementOperation? _recoveryOperation;
    private OriginalBackupState? _backupState;
    private FinalizationTransaction? _finalizationTransaction;
    private CancellationTokenSource? _copyCancellation;
    private CancellationTokenSource? _backupCancellation;
    private bool _copyInProgress;
    private bool _backupInProgress;
    private bool _promotionInProgress;
    private bool _sourceRecycleInProgress;
    private bool _completionInProgress;
    private bool _oneClickInProgress;
    private bool _undoInProgress;
    private bool _closeWhenFinished;

    public ReplacementReviewWindow(
        ReplacementPlan plan,
        TemporaryCopyService temporaryCopyService,
        TemporaryCopyCleanupService temporaryCopyCleanupService,
        ReplacementOperationRepository operationRepository,
        OriginalBackupRepository backupRepository,
        OriginalBackupService originalBackupService,
        OriginalBackupCleanupService originalBackupCleanupService,
        FinalizationReadinessService finalizationReadinessService,
        FinalizationTransactionRepository finalizationTransactionRepository,
        FinalizationPreparationService finalizationPreparationService,
        FinalizationPromotionService finalizationPromotionService,
        SourceRecycleService sourceRecycleService,
        FinalizationCompletionService finalizationCompletionService,
        SafeReplacementService safeReplacementService,
        UndoPreparationService undoPreparationService,
        SourceRestorationService sourceRestorationService,
        FinalFileRecycleService finalFileRecycleService,
        UndoCompletionService undoCompletionService)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(temporaryCopyService);
        ArgumentNullException.ThrowIfNull(temporaryCopyCleanupService);
        ArgumentNullException.ThrowIfNull(operationRepository);
        ArgumentNullException.ThrowIfNull(backupRepository);
        ArgumentNullException.ThrowIfNull(originalBackupService);
        ArgumentNullException.ThrowIfNull(originalBackupCleanupService);
        ArgumentNullException.ThrowIfNull(finalizationReadinessService);
        ArgumentNullException.ThrowIfNull(finalizationTransactionRepository);
        ArgumentNullException.ThrowIfNull(finalizationPreparationService);
        ArgumentNullException.ThrowIfNull(finalizationPromotionService);
        ArgumentNullException.ThrowIfNull(sourceRecycleService);
        ArgumentNullException.ThrowIfNull(finalizationCompletionService);
        ArgumentNullException.ThrowIfNull(safeReplacementService);
        ArgumentNullException.ThrowIfNull(undoPreparationService);
        ArgumentNullException.ThrowIfNull(sourceRestorationService);
        ArgumentNullException.ThrowIfNull(finalFileRecycleService);
        ArgumentNullException.ThrowIfNull(undoCompletionService);
        _plan = plan;
        _temporaryCopyService = temporaryCopyService;
        _temporaryCopyCleanupService = temporaryCopyCleanupService;
        _operationRepository = operationRepository;
        _backupRepository = backupRepository;
        _originalBackupService = originalBackupService;
        _originalBackupCleanupService = originalBackupCleanupService;
        _finalizationReadinessService = finalizationReadinessService;
        _finalizationTransactionRepository = finalizationTransactionRepository;
        _finalizationPreparationService = finalizationPreparationService;
        _finalizationPromotionService = finalizationPromotionService;
        _sourceRecycleService = sourceRecycleService;
        _finalizationCompletionService = finalizationCompletionService;
        _safeReplacementService = safeReplacementService;
        _undoPreparationService = undoPreparationService;
        _sourceRestorationService = sourceRestorationService;
        _finalFileRecycleService = finalFileRecycleService;
        _undoCompletionService = undoCompletionService;
        InitializeComponent();
        PopupWindowRendering.Stabilize(this);
        ApplyPlan(plan);
        PrepareCopyButton.IsEnabled = false;
        CopyStatusText.Text = "Checking previous operation state...";
        Loaded += ReplacementReviewWindow_Loaded;
        Closing += ReplacementReviewWindow_Closing;
    }

    public TemporaryCopyResult? CopyResult { get; private set; }
    public TemporaryCopyCleanupResult? CleanupResult { get; private set; }
    public bool CopyWasCancelled { get; private set; }
    public Exception? CopyFailure { get; private set; }
    public Exception? CleanupFailure { get; private set; }
    public OriginalBackupResult? BackupResult { get; private set; }
    public OriginalBackupCleanupResult? BackupCleanupResult { get; private set; }
    public bool BackupWasCancelled { get; private set; }
    public Exception? BackupFailure { get; private set; }
    public Exception? BackupCleanupFailure { get; private set; }
    public FinalizationPromotionResult? PromotionResult { get; private set; }
    public Exception? PromotionFailure { get; private set; }
    public SourceRecycleResult? SourceRecycleResult { get; private set; }
    public Exception? SourceRecycleFailure { get; private set; }
    public FinalizationCompletionResult? FinalizationCompletionResult { get; private set; }
    public Exception? FinalizationCompletionFailure { get; private set; }
    public SafeReplacementResult? SafeReplacementResult { get; private set; }
    public Exception? SafeReplacementFailure { get; private set; }
    public UndoPreparationResult? UndoPreparationResult { get; private set; }
    public SourceRestorationResult? SourceRestorationResult { get; private set; }
    public FinalFileRecycleResult? FinalFileRecycleResult { get; private set; }
    public UndoCompletionResult? UndoCompletionResult { get; private set; }
    public Exception? UndoFailure { get; private set; }

    private void ApplyPlan(ReplacementPlan plan)
    {
        SourcePathTextBox.Text = plan.CompletedEncode.SourcePath;
        DestinationPathTextBox.Text = plan.CompletedEncode.DestinationPath;
        FinalPathTextBox.Text = plan.Paths.FinalPath;
        TemporaryPathTextBox.Text = plan.Paths.TemporaryPath;
        BackupPathTextBox.Text = plan.Paths.BackupPath;
        SizesText.Text = $"Source {FormatBytes(plan.Snapshot.SourceSize)}  |  " +
                         $"Converted {FormatBytes(plan.Snapshot.DestinationSize)}";
        OutcomeText.Text = plan.CanProceed
            ? "Preflight checks passed. Review the paths and warnings, then use Replace source safely to run the complete guarded workflow with one confirmation."
            : "Preparation is blocked. Resolve every blocking item before creating a temporary copy.";
        OutcomeText.Foreground = plan.CanProceed
            ? System.Windows.Media.Brushes.DarkGreen
            : System.Windows.Media.Brushes.DarkRed;
        IssuesList.ItemsSource = plan.Issues.Count == 0
            ? ["No blocking issues or warnings were found."]
            : plan.Issues.Select(issue =>
                $"{(issue.Severity == ReplacementIssueSeverity.Blocking ? "BLOCKING" : "WARNING")}: {issue.Message}")
                .ToArray();
    }

    private async void ReplacementReviewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshRecoveryStateAsync(updateCopyStatus: true);
    }

    private async Task RefreshRecoveryStateAsync(bool updateCopyStatus)
    {
        try
        {
            _plan = _preflightService.Review(_plan.CompletedEncode);
            ApplyPlan(_plan);
            var canPrepare = _plan.CanProceed;
            _recoveryOperation = null;
            _backupState = null;
            _finalizationTransaction = null;
            DiscardTemporaryButton.IsEnabled = false;
            CreateBackupButton.IsEnabled = false;
            DiscardBackupButton.IsEnabled = false;
            CheckFinalizationButton.IsEnabled = false;
            PromoteFinalButton.IsEnabled = false;
            RecycleSourceButton.IsEnabled = false;
            CompleteFinalizationButton.IsEnabled = false;
            BeginUndoButton.IsEnabled = false;
            RestoreSourceButton.IsEnabled = false;
            RecycleFinalButton.IsEnabled = false;
            CompleteUndoButton.IsEnabled = false;
            SafeReplaceButton.IsEnabled = false;
            RecoveryStatusText.Visibility = Visibility.Collapsed;
            await _operationRepository.InitializeAsync();
            var operation = await _operationRepository.GetLatestForCompletedEncodeAsync(
                _plan.CompletedEncode.Id);
            if (operation is not null &&
                string.Equals(
                    operation.TemporaryPath,
                    _plan.Paths.TemporaryPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                var recoveryReview = ReplacementRecoveryAdvisor.Review(
                    operation,
                    File.Exists(operation.TemporaryPath));
                if (recoveryReview.ShouldDisplay)
                {
                    _recoveryOperation = operation;
                    ShowRecoveryState(recoveryReview);
                    canPrepare &= !recoveryReview.BlocksNewCopy;
                    DiscardTemporaryButton.IsEnabled =
                        File.Exists(operation.TemporaryPath) &&
                        operation.Status != ReplacementOperationStatus.Completed &&
                        !_copyInProgress &&
                        !_backupInProgress;
                }

                _backupState = await _backupRepository.GetAsync(operation.Id);
                _finalizationTransaction = await _finalizationTransactionRepository.GetAsync(operation.Id);
                if (_finalizationTransaction is not null)
                {
                    _recoveryOperation = operation;
                    DiscardTemporaryButton.IsEnabled = false;
                }
                ApplyBackupState(operation, _backupState, _finalizationTransaction);
            }

            PrepareCopyButton.IsEnabled = canPrepare && !_copyInProgress && !_backupInProgress;
            SafeReplaceButton.IsEnabled = canPrepare &&
                !_copyInProgress && !_backupInProgress && !_oneClickInProgress &&
                _finalizationTransaction is null;
            if (updateCopyStatus)
            {
                CopyStatusText.Text = canPrepare
                    ? "Ready to create a verified temporary copy."
                    : "Temporary-copy preparation is not available until the displayed safety or recovery issue is resolved.";
            }
        }
        catch (Exception exception)
        {
            PrepareCopyButton.IsEnabled = false;
            DiscardTemporaryButton.IsEnabled = false;
            CreateBackupButton.IsEnabled = false;
            DiscardBackupButton.IsEnabled = false;
            CheckFinalizationButton.IsEnabled = false;
            PromoteFinalButton.IsEnabled = false;
            RecycleSourceButton.IsEnabled = false;
            CompleteFinalizationButton.IsEnabled = false;
            BeginUndoButton.IsEnabled = false;
            RestoreSourceButton.IsEnabled = false;
            RecycleFinalButton.IsEnabled = false;
            CompleteUndoButton.IsEnabled = false;
            SafeReplaceButton.IsEnabled = false;
            if (updateCopyStatus)
            {
                CopyStatusText.Text = "Temporary-copy preparation is disabled because recovery state could not be checked.";
            }
            RecoveryStatusText.Text = $"Existing operation state could not be read: {exception.Message}";
            RecoveryStatusText.Visibility = Visibility.Visible;
        }
    }

    private void ApplyBackupState(
        ReplacementOperation operation,
        OriginalBackupState? backup,
        FinalizationTransaction? finalizationTransaction)
    {
        var verifiedTemporary =
            operation.Status == ReplacementOperationStatus.InProgress &&
            operation.VerificationStatus == ReplacementVerificationStatus.Verified &&
            File.Exists(operation.TemporaryPath) &&
            new FileInfo(operation.TemporaryPath).Length == operation.DestinationSize;
        var backupFileExists = backup is not null && File.Exists(backup.BackupPath);
        if (backup is null)
        {
            BackupStatusText.Text = File.Exists(operation.BackupPath) || Directory.Exists(operation.BackupPath)
                ? "The planned original-backup path is already occupied and will not be overwritten."
                : verifiedTemporary
                    ? "Verified temporary copy is ready. The original source can now be copied to its backup path."
                    : "Waiting for a verified temporary copy.";
        }
        else
        {
            BackupStatusText.Text = backup.Status switch
            {
                OriginalBackupStatus.Verified when backupFileExists =>
                    $"Original backup verified ({FormatBytes(backup.BytesCopied)}). SHA-256: {backup.Sha256}",
                OriginalBackupStatus.Failed =>
                    $"Original backup failed after {FormatBytes(backup.BytesCopied)}. {backup.FailureMessage}",
                OriginalBackupStatus.Cancelled =>
                    $"Original backup was cancelled after {FormatBytes(backup.BytesCopied)}.",
                _ when backupFileExists =>
                    $"Incomplete original-backup state found: {backup.Status} at {FormatBytes(backup.BytesCopied)}.",
                _ =>
                    $"Original-backup state is {backup.Status}, but its file is missing. Recovery review is required."
            };
        }

        var retryableState = backup is null ||
            backup.Status is OriginalBackupStatus.Failed or OriginalBackupStatus.Cancelled;
        CreateBackupButton.IsEnabled =
            verifiedTemporary &&
            operation.Stage == ReplacementOperationStage.Verifying &&
            retryableState &&
            !backupFileExists &&
            !File.Exists(operation.BackupPath) &&
            !Directory.Exists(operation.BackupPath) &&
            !_copyInProgress &&
            !_backupInProgress;
        DiscardBackupButton.IsEnabled =
            backupFileExists &&
            finalizationTransaction is null &&
            !_copyInProgress &&
            !_backupInProgress;
        CheckFinalizationButton.IsEnabled =
            verifiedTemporary &&
            backupFileExists &&
            backup?.Status == OriginalBackupStatus.Verified &&
            operation.Stage == ReplacementOperationStage.BackingUpSource &&
            !_copyInProgress &&
            !_backupInProgress;
        PromoteFinalButton.IsEnabled =
            (finalizationTransaction?.Checkpoint is
                FinalizationCheckpoint.Prepared or
                FinalizationCheckpoint.PromoteTemporaryIntentRecorded) &&
            !_copyInProgress &&
            !_backupInProgress &&
            !_promotionInProgress &&
            !_sourceRecycleInProgress &&
            !_completionInProgress &&
            !_undoInProgress;
        PromoteFinalButton.Content = finalizationTransaction?.Checkpoint == FinalizationCheckpoint.PromoteTemporaryIntentRecorded
            ? "Recover promotion"
            : "Promote verified copy";
        RecycleSourceButton.IsEnabled =
            (finalizationTransaction?.Checkpoint is
                FinalizationCheckpoint.FinalPromoted or
                FinalizationCheckpoint.RecycleSourceIntentRecorded) &&
            !_copyInProgress &&
            !_backupInProgress &&
            !_promotionInProgress &&
            !_sourceRecycleInProgress &&
            !_completionInProgress &&
            !_undoInProgress;
        RecycleSourceButton.Content = finalizationTransaction?.Checkpoint == FinalizationCheckpoint.RecycleSourceIntentRecorded
            ? "Recover source recycle"
            : "Recycle original source";
        CompleteFinalizationButton.IsEnabled =
            finalizationTransaction?.Checkpoint == FinalizationCheckpoint.SourceRecycled &&
            !_copyInProgress &&
            !_backupInProgress &&
            !_promotionInProgress &&
            !_sourceRecycleInProgress &&
            !_completionInProgress &&
            !_undoInProgress;
        BeginUndoButton.IsEnabled =
            finalizationTransaction?.Checkpoint == FinalizationCheckpoint.Completed &&
            !_copyInProgress && !_backupInProgress && !_promotionInProgress &&
            !_sourceRecycleInProgress && !_completionInProgress && !_undoInProgress;
        RestoreSourceButton.IsEnabled =
            finalizationTransaction?.Checkpoint is
                FinalizationCheckpoint.UndoPrepared or FinalizationCheckpoint.RestoreSourceIntentRecorded &&
            !_copyInProgress && !_backupInProgress && !_promotionInProgress &&
            !_sourceRecycleInProgress && !_completionInProgress && !_undoInProgress;
        RestoreSourceButton.Content = finalizationTransaction?.Checkpoint == FinalizationCheckpoint.RestoreSourceIntentRecorded
            ? "Recover source restore"
            : "Restore original source";
        RecycleFinalButton.IsEnabled =
            finalizationTransaction?.Checkpoint is
                FinalizationCheckpoint.SourceRestored or FinalizationCheckpoint.RecycleFinalIntentRecorded &&
            !_copyInProgress && !_backupInProgress && !_promotionInProgress &&
            !_sourceRecycleInProgress && !_completionInProgress && !_undoInProgress;
        RecycleFinalButton.Content = finalizationTransaction?.Checkpoint == FinalizationCheckpoint.RecycleFinalIntentRecorded
            ? "Recover final recycle"
            : "Recycle promoted final";
        CompleteUndoButton.IsEnabled =
            finalizationTransaction?.Checkpoint == FinalizationCheckpoint.FinalRecycled &&
            !_copyInProgress && !_backupInProgress && !_promotionInProgress &&
            !_sourceRecycleInProgress && !_completionInProgress && !_undoInProgress;
        FinalizationStatusText.Foreground = finalizationTransaction?.Checkpoint switch
        {
            FinalizationCheckpoint.FinalPromoted or FinalizationCheckpoint.SourceRecycled or FinalizationCheckpoint.Completed or
            FinalizationCheckpoint.UndoPrepared or FinalizationCheckpoint.SourceRestored or FinalizationCheckpoint.FinalRecycled or
            FinalizationCheckpoint.Undone
                when string.IsNullOrWhiteSpace(finalizationTransaction.FailureMessage) => System.Windows.Media.Brushes.DarkGreen,
            FinalizationCheckpoint.PromoteTemporaryIntentRecorded when !string.IsNullOrWhiteSpace(finalizationTransaction.FailureMessage) =>
                System.Windows.Media.Brushes.DarkRed,
            FinalizationCheckpoint.RecycleSourceIntentRecorded when !string.IsNullOrWhiteSpace(finalizationTransaction.FailureMessage) =>
                System.Windows.Media.Brushes.DarkRed,
            _ => System.Windows.Media.Brushes.Black
        };
        FinalizationStatusText.Text = finalizationTransaction?.Checkpoint switch
        {
            FinalizationCheckpoint.Prepared =>
                $"Transaction checkpoint: Prepared (revision {finalizationTransaction.Revision}). Atomic promotion is available; the source will remain untouched.",
            FinalizationCheckpoint.PromoteTemporaryIntentRecorded =>
                $"Promotion recovery is required (revision {finalizationTransaction.Revision}). " +
                $"{finalizationTransaction.FailureMessage ?? "The current artifacts will be verified before continuing."}",
            FinalizationCheckpoint.FinalPromoted =>
                "Final file promoted and verified. After separate confirmation, the verified original source can be moved to the Windows Recycle Bin.",
            FinalizationCheckpoint.RecycleSourceIntentRecorded =>
                $"Source recycling recovery is required (revision {finalizationTransaction.Revision}). " +
                $"{finalizationTransaction.FailureMessage ?? "The source, backup, and final file will be checked before continuing."}",
            FinalizationCheckpoint.SourceRecycled =>
                $"Original source moved to the Windows Recycle Bin. Complete finalisation to atomically close the transaction after one last backup/final integrity check. " +
                $"{finalizationTransaction.FailureMessage ?? string.Empty}",
            FinalizationCheckpoint.Completed =>
                "Replacement finalisation completed. Prepare undo to revalidate the final file and backup before restoring the source.",
            FinalizationCheckpoint.UndoPrepared =>
                "Undo prepared. Restore and verify the original source before the promoted final file can be touched.",
            FinalizationCheckpoint.RestoreSourceIntentRecorded =>
                $"Source restoration recovery is required. {finalizationTransaction.FailureMessage ?? "The restore artifact will be verified before continuing."}",
            FinalizationCheckpoint.SourceRestored =>
                "Original source restored and verified. The promoted final may now be moved to the Windows Recycle Bin after separate confirmation.",
            FinalizationCheckpoint.RecycleFinalIntentRecorded =>
                $"Promoted-final recycling recovery is required. {finalizationTransaction.FailureMessage ?? "The source and backup will be verified before continuing."}",
            FinalizationCheckpoint.FinalRecycled =>
                "Promoted final moved to the Windows Recycle Bin. Complete undo to atomically restore source availability in history.",
            FinalizationCheckpoint.Undone =>
                "Undo completed. The original source and verified backup remain available; the promoted final is in the Windows Recycle Bin.",
            not null =>
                $"Transaction checkpoint: {finalizationTransaction.Checkpoint} (revision {finalizationTransaction.Revision}). Further file execution is disabled.",
            _ when CheckFinalizationButton.IsEnabled =>
                "Both artifacts are verified. Run the file-read-only readiness check to prepare a durable transaction design.",
            _ => "Finalisation remains disabled until verified temporary and original-backup artifacts are both present."
        };
    }

    private async void PrepareCopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_copyInProgress || !_plan.CanProceed)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            "Create and verify this temporary copy?\n\n" +
            _plan.Paths.TemporaryPath +
            "\n\nThe source and converted files will not be changed. " +
            "If copying is cancelled or fails, any partial temporary file is retained for recovery review.",
            "Confirm temporary copy",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _copyInProgress = true;
        CopyResult = null;
        CleanupResult = null;
        CopyWasCancelled = false;
        CopyFailure = null;
        CleanupFailure = null;
        BackupResult = null;
        BackupCleanupResult = null;
        BackupWasCancelled = false;
        BackupFailure = null;
        BackupCleanupFailure = null;
        PrepareCopyButton.IsEnabled = false;
        CancelCopyButton.IsEnabled = true;
        DiscardTemporaryButton.IsEnabled = false;
        CreateBackupButton.IsEnabled = false;
        DiscardBackupButton.IsEnabled = false;
        CopyProgressBar.Value = 0;
        CopyStatusText.Text = "Preparing temporary copy...";
        _copyCancellation = new CancellationTokenSource();
        var progress = new Progress<ReplacementCopyProgress>(update =>
        {
            CopyProgressBar.Value = Math.Clamp(update.Percentage, 0, 100);
            CopyStatusText.Text =
                $"Copying {FormatBytes(update.BytesCopied)} of {FormatBytes(update.TotalBytes)} " +
                $"({update.Percentage:0.0}%).";
        });

        try
        {
            CopyResult = await _temporaryCopyService.CopyAndVerifyAsync(
                _plan,
                progress,
                _copyCancellation.Token);
            CopyProgressBar.Value = 100;
            CopyStatusText.Text =
                $"Temporary copy verified ({FormatBytes(CopyResult.BytesCopied)}). " +
                $"SHA-256: {CopyResult.Sha256}";
            CopyStatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            RecoveryStatusText.Text =
                "The verified temporary copy is retained. Prepare the separate original backup before finalisation readiness can be checked.";
            RecoveryStatusText.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            CopyWasCancelled = true;
            CopyStatusText.Text =
                "Copy cancelled. Any partial temporary file was retained for recovery review; no original file was changed.";
            CopyStatusText.Foreground = System.Windows.Media.Brushes.DarkGoldenrod;
        }
        catch (Exception exception)
        {
            CopyFailure = exception;
            CopyStatusText.Text =
                $"Temporary copy failed: {exception.Message} Any partial file was retained; no original file was changed.";
            CopyStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
        finally
        {
            _copyInProgress = false;
            CancelCopyButton.IsEnabled = false;
            await RefreshRecoveryStateAsync(updateCopyStatus: false);
            _copyCancellation.Dispose();
            _copyCancellation = null;
            if (_closeWhenFinished)
            {
                Close();
            }
        }
    }

    private async void DiscardTemporaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_copyInProgress ||
            _recoveryOperation is null ||
            !File.Exists(_recoveryOperation.TemporaryPath))
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            "Permanently discard only this temporary-copy file?\n\n" +
            _recoveryOperation.TemporaryPath +
            "\n\nThis cannot be undone. The source, converted output, planned final file, and backup path will not be changed.",
            "Confirm temporary-file cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        PrepareCopyButton.IsEnabled = false;
        DiscardTemporaryButton.IsEnabled = false;
        CleanupResult = null;
        CleanupFailure = null;
        try
        {
            CleanupResult = await _temporaryCopyCleanupService.DiscardAsync(
                _plan,
                _recoveryOperation);
            await RefreshRecoveryStateAsync(updateCopyStatus: false);
            CopyProgressBar.Value = 0;
            CopyStatusText.Text = _plan.CanProceed
                ? $"Temporary file discarded ({FormatBytes(CleanupResult.BytesRemoved)}). " +
                  "A fresh preflight passed and the copy can now be retried."
                : $"Temporary file discarded ({FormatBytes(CleanupResult.BytesRemoved)}). " +
                  "The fresh preflight found another issue that must be resolved before retrying.";
            CopyStatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        catch (Exception exception)
        {
            CleanupFailure = exception;
            CopyStatusText.Text =
                $"Temporary-file cleanup was refused or failed: {exception.Message} No original file was changed.";
            CopyStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            await RefreshRecoveryStateAsync(updateCopyStatus: false);
        }
    }

    private async void CreateBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_copyInProgress ||
            _backupInProgress ||
            _recoveryOperation is null ||
            !CreateBackupButton.IsEnabled)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            "Create and verify this original-backup copy?\n\n" +
            _recoveryOperation.BackupPath +
            "\n\nThe source will be read and copied, not moved, renamed, or deleted. " +
            "If copying stops, any partial backup is retained for recovery review.",
            "Confirm original backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _backupInProgress = true;
        BackupResult = null;
        BackupCleanupResult = null;
        BackupWasCancelled = false;
        BackupFailure = null;
        BackupCleanupFailure = null;
        PrepareCopyButton.IsEnabled = false;
        DiscardTemporaryButton.IsEnabled = false;
        CreateBackupButton.IsEnabled = false;
        DiscardBackupButton.IsEnabled = false;
        CancelBackupButton.IsEnabled = true;
        BackupProgressBar.Value = 0;
        BackupStatusText.Text = "Preparing original-backup copy...";
        _backupCancellation = new CancellationTokenSource();
        var progress = new Progress<OriginalBackupProgress>(update =>
        {
            BackupProgressBar.Value = Math.Clamp(update.Percentage, 0, 100);
            BackupStatusText.Text =
                $"Backing up {FormatBytes(update.BytesCopied)} of {FormatBytes(update.TotalBytes)} " +
                $"({update.Percentage:0.0}%).";
        });

        try
        {
            BackupResult = await _originalBackupService.CopyAndVerifyAsync(
                _plan,
                _recoveryOperation,
                progress,
                _backupCancellation.Token);
            BackupProgressBar.Value = 100;
            BackupStatusText.Text =
                $"Original backup verified ({FormatBytes(BackupResult.BytesCopied)}). " +
                $"SHA-256: {BackupResult.Sha256}";
            BackupStatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        catch (OperationCanceledException)
        {
            BackupWasCancelled = true;
            BackupStatusText.Text =
                "Original backup cancelled. Any partial backup was retained; the source was unchanged.";
            BackupStatusText.Foreground = System.Windows.Media.Brushes.DarkGoldenrod;
        }
        catch (Exception exception)
        {
            BackupFailure = exception;
            BackupStatusText.Text =
                $"Original backup failed: {exception.Message} Any partial backup was retained; the source was unchanged.";
            BackupStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
        finally
        {
            _backupInProgress = false;
            CancelBackupButton.IsEnabled = false;
            await RefreshRecoveryStateAsync(updateCopyStatus: false);
            _backupCancellation.Dispose();
            _backupCancellation = null;
            if (_closeWhenFinished)
            {
                Close();
            }
        }
    }

    private void CancelBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_backupInProgress || _backupCancellation is null)
        {
            return;
        }

        CancelBackupButton.IsEnabled = false;
        BackupStatusText.Text = "Backup cancellation requested. The current write will stop safely...";
        _backupCancellation.Cancel();
    }

    private async void DiscardBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_copyInProgress ||
            _backupInProgress ||
            _recoveryOperation is null ||
            _backupState is null ||
            !File.Exists(_backupState.BackupPath))
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            "Permanently discard only this original-backup artifact?\n\n" +
            _backupState.BackupPath +
            "\n\nThis cannot be undone. The source, converted output, and verified temporary copy will not be changed.",
            "Confirm backup-file cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        CreateBackupButton.IsEnabled = false;
        DiscardBackupButton.IsEnabled = false;
        BackupCleanupResult = null;
        BackupCleanupFailure = null;
        try
        {
            BackupCleanupResult = await _originalBackupCleanupService.DiscardAsync(
                _plan,
                _recoveryOperation,
                _backupState);
            await RefreshRecoveryStateAsync(updateCopyStatus: false);
            BackupProgressBar.Value = 0;
            BackupStatusText.Text =
                $"Original-backup artifact discarded ({FormatBytes(BackupCleanupResult.BytesRemoved)}). " +
                "The verified temporary copy and original source remain unchanged; backup may be retried.";
            BackupStatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        catch (Exception exception)
        {
            BackupCleanupFailure = exception;
            BackupStatusText.Text =
                $"Backup-file cleanup was refused or failed: {exception.Message} The source was unchanged.";
            BackupStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            await RefreshRecoveryStateAsync(updateCopyStatus: false);
        }
    }

    private async void SafeReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SafeReplaceButton.IsEnabled ||
            _oneClickInProgress || _copyInProgress || _backupInProgress ||
            _promotionInProgress || _sourceRecycleInProgress || _completionInProgress || _undoInProgress)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            "Replace the original source with the verified HandBrake output?\n\n" +
            $"Original source:\n{_plan.CompletedEncode.SourcePath}\n\n" +
            $"Converted output:\n{_plan.CompletedEncode.DestinationPath}\n\n" +
            $"New file beside the source:\n{_plan.Paths.FinalPath}\n\n" +
            $"Retained original backup:\n{_plan.Paths.BackupPath}\n\n" +
            "The application will copy and verify the converted file, create and verify the backup, " +
            "promote the converted copy without overwriting a file, and then move the original source to the Windows Recycle Bin. " +
            "The existing converted output is not moved or deleted. If any check fails, processing stops at a recoverable checkpoint.",
            "Replace source safely",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            FinalizationStatusText.Text = "One-click replacement cancelled. No files or recovery checkpoints were changed.";
            return;
        }

        SetOneClickBusy();
        var progress = new Progress<SafeReplacementProgress>(UpdateOneClickProgress);
        try
        {
            SafeReplacementResult = await _safeReplacementService.ReplaceAsync(_plan, progress);
            SafeReplacementFailure = null;
        }
        catch (Exception exception)
        {
            SafeReplacementFailure = exception;
        }
        finally
        {
            _oneClickInProgress = false;
            await RefreshRecoveryStateAsync(updateCopyStatus: false);
            if (SafeReplacementResult is not null)
            {
                CopyProgressBar.Value = 100;
                BackupProgressBar.Value = 100;
                FinalizationStatusText.Text =
                    "Replacement completed safely. The verified converted file is now beside the former source, " +
                    "the original source is in the Windows Recycle Bin, and its verified backup remains available for undo.";
                FinalizationStatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            }
            else if (SafeReplacementFailure is not null)
            {
                FinalizationStatusText.Text =
                    $"One-click replacement stopped safely: {SafeReplacementFailure.Message} " +
                    "Use Recovery to continue from the recorded checkpoint.";
                FinalizationStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            }
        }
    }

    private void SetOneClickBusy()
    {
        _oneClickInProgress = true;
        SafeReplacementResult = null;
        SafeReplacementFailure = null;
        CopyResult = null;
        CleanupResult = null;
        CopyWasCancelled = false;
        CopyFailure = null;
        CleanupFailure = null;
        BackupResult = null;
        BackupCleanupResult = null;
        BackupWasCancelled = false;
        BackupFailure = null;
        BackupCleanupFailure = null;
        PromotionResult = null;
        PromotionFailure = null;
        SourceRecycleResult = null;
        SourceRecycleFailure = null;
        FinalizationCompletionResult = null;
        FinalizationCompletionFailure = null;
        UndoPreparationResult = null;
        SourceRestorationResult = null;
        FinalFileRecycleResult = null;
        UndoCompletionResult = null;
        UndoFailure = null;
        SafeReplaceButton.IsEnabled = false;
        PrepareCopyButton.IsEnabled = false;
        CancelCopyButton.IsEnabled = false;
        DiscardTemporaryButton.IsEnabled = false;
        CreateBackupButton.IsEnabled = false;
        CancelBackupButton.IsEnabled = false;
        DiscardBackupButton.IsEnabled = false;
        CheckFinalizationButton.IsEnabled = false;
        PromoteFinalButton.IsEnabled = false;
        RecycleSourceButton.IsEnabled = false;
        CompleteFinalizationButton.IsEnabled = false;
        BeginUndoButton.IsEnabled = false;
        RestoreSourceButton.IsEnabled = false;
        RecycleFinalButton.IsEnabled = false;
        CompleteUndoButton.IsEnabled = false;
        FinalizationStatusText.Text = "Starting the confirmed safe replacement...";
        FinalizationStatusText.Foreground = System.Windows.Media.Brushes.Black;
    }

    private void UpdateOneClickProgress(SafeReplacementProgress progress)
    {
        if (progress.Stage == SafeReplacementStage.CopyingConvertedFile && progress.Percentage is double copy)
        {
            CopyProgressBar.Value = Math.Clamp(copy, 0, 100);
            CopyStatusText.Text = $"{progress.Message} {copy:0.0}%";
        }
        else if (progress.Stage == SafeReplacementStage.BackingUpOriginalSource && progress.Percentage is double backup)
        {
            CopyProgressBar.Value = 100;
            BackupProgressBar.Value = Math.Clamp(backup, 0, 100);
            BackupStatusText.Text = $"{progress.Message} {backup:0.0}%";
        }

        FinalizationStatusText.Text = progress.Message;
    }

    private async void CheckFinalizationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recoveryOperation is null ||
            _backupState is null ||
            !CheckFinalizationButton.IsEnabled)
        {
            return;
        }

        CheckFinalizationButton.IsEnabled = false;
        FinalizationStatusText.Text = "Checking all persisted states, paths, sizes, and SHA-256 equivalence...";
        try
        {
            var readiness = await _finalizationReadinessService.ReviewAsync(
                _plan,
                _recoveryOperation,
                _backupState);
            _finalizationTransaction = readiness.IsReady
                ? await _finalizationPreparationService.PrepareAsync(_recoveryOperation, readiness)
                : null;
            if (_finalizationTransaction is not null)
            {
                DiscardTemporaryButton.IsEnabled = false;
                DiscardBackupButton.IsEnabled = false;
                PromoteFinalButton.IsEnabled =
                    _finalizationTransaction.Checkpoint is
                        FinalizationCheckpoint.Prepared or
                        FinalizationCheckpoint.PromoteTemporaryIntentRecorded;
            }
            FinalizationStatusText.Text = readiness.IsReady
                ? $"READY — transaction checkpoint {_finalizationTransaction!.Checkpoint} was persisted. Atomic promotion is available; source recycling requires a later separate confirmation."
                : "BLOCKED — " + string.Join(" ", readiness.Issues.Select(issue => issue.Message));
            FinalizationStatusText.Foreground = readiness.IsReady
                ? System.Windows.Media.Brushes.DarkGreen
                : System.Windows.Media.Brushes.DarkRed;
        }
        catch (OperationCanceledException)
        {
            FinalizationStatusText.Text = "Finalisation readiness check cancelled.";
        }
        catch (Exception exception)
        {
            FinalizationStatusText.Text = $"Finalisation readiness check failed safely: {exception.Message}";
            FinalizationStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
        finally
        {
            CheckFinalizationButton.IsEnabled =
                _recoveryOperation.Stage == ReplacementOperationStage.BackingUpSource &&
                _recoveryOperation.VerificationStatus == ReplacementVerificationStatus.Verified &&
                _backupState.Status == OriginalBackupStatus.Verified &&
                File.Exists(_recoveryOperation.TemporaryPath) &&
                File.Exists(_backupState.BackupPath) &&
                !_copyInProgress &&
                !_backupInProgress;
        }
    }

    private async void PromoteFinalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recoveryOperation is null ||
            _finalizationTransaction is null ||
            !PromoteFinalButton.IsEnabled ||
            _promotionInProgress)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            "Atomically promote the verified temporary copy?\n\n" +
            $"Temporary: {_recoveryOperation.TemporaryPath}\n" +
            $"Final: {_recoveryOperation.FinalPath}\n\n" +
            $"The original source will remain unchanged at:\n{_recoveryOperation.SourcePath}\n\n" +
            "The final path must be empty and will never be overwritten.",
            "Promote verified copy",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            FinalizationStatusText.Text = "Atomic promotion cancelled. No files or transaction checkpoints were changed.";
            return;
        }

        _promotionInProgress = true;
        PromoteFinalButton.IsEnabled = false;
        CheckFinalizationButton.IsEnabled = false;
        FinalizationStatusText.Text = "Revalidating protected artifacts and recording atomic-promotion intent...";
        try
        {
            PromotionResult = await _finalizationPromotionService.PromoteAsync(_recoveryOperation.Id);
            FinalizationStatusText.Text = PromotionResult.WasRecovered
                ? "Promotion recovery verified the final file and recorded completion. The source and backup are unchanged."
                : "Verified copy promoted atomically. The source and backup are unchanged; source recycling now requires separate confirmation.";
            FinalizationStatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        catch (Exception exception)
        {
            PromotionFailure = exception;
            FinalizationStatusText.Text =
                $"Atomic promotion stopped safely: {exception.Message} Recovery review will inspect the recorded checkpoint.";
            FinalizationStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
        finally
        {
            _promotionInProgress = false;
            await RefreshRecoveryStateAsync(updateCopyStatus: false);
        }
    }

    private async void RecycleSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recoveryOperation is null ||
            _finalizationTransaction is null ||
            !RecycleSourceButton.IsEnabled ||
            _sourceRecycleInProgress)
        {
            return;
        }

        var recovery = _finalizationTransaction.Checkpoint == FinalizationCheckpoint.RecycleSourceIntentRecorded;
        var confirmation = System.Windows.MessageBox.Show(
            this,
            (recovery
                ? "Recover the interrupted source Recycle Bin operation?\n\n"
                : "Move the verified original source to the Windows Recycle Bin?\n\n") +
            $"Source: {_recoveryOperation.SourcePath}\n\n" +
            $"Promoted final: {_recoveryOperation.FinalPath}\n" +
            $"Verified backup: {_recoveryOperation.BackupPath}\n\n" +
            "The source path will become empty, but Windows will retain the file in the Recycle Bin. " +
            "If Windows cannot guarantee a Recycle Bin operation, the action fails instead of deleting permanently. " +
            "The promoted final file and verified backup will not be changed.",
            recovery ? "Recover source recycling" : "Recycle verified original source",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            FinalizationStatusText.Text = "Source recycling cancelled. No files or transaction checkpoints were changed.";
            return;
        }

        _sourceRecycleInProgress = true;
        SourceRecycleResult = null;
        SourceRecycleFailure = null;
        RecycleSourceButton.IsEnabled = false;
        PromoteFinalButton.IsEnabled = false;
        CheckFinalizationButton.IsEnabled = false;
        FinalizationStatusText.Text = "Revalidating the source, backup, and final file before recording Recycle Bin intent...";
        try
        {
            SourceRecycleResult = await _sourceRecycleService.RecycleAsync(_recoveryOperation.Id);
            FinalizationStatusText.Text = SourceRecycleResult.WasRecovered
                ? "Recovery verified that the source left its path and recorded source recycling. The backup and final file are unchanged."
                : "Original source moved to the Windows Recycle Bin. The backup and promoted final file are unchanged.";
            FinalizationStatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        catch (Exception exception)
        {
            SourceRecycleFailure = exception;
            FinalizationStatusText.Text =
                $"Source recycling stopped safely: {exception.Message} Recovery review will inspect the recorded checkpoint.";
            FinalizationStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
        finally
        {
            _sourceRecycleInProgress = false;
            await RefreshRecoveryStateAsync(updateCopyStatus: false);
        }
    }

    private async void CompleteFinalizationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recoveryOperation is null ||
            _finalizationTransaction is null ||
            !CompleteFinalizationButton.IsEnabled ||
            _completionInProgress)
        {
            return;
        }

        _completionInProgress = true;
        FinalizationCompletionResult = null;
        FinalizationCompletionFailure = null;
        CompleteFinalizationButton.IsEnabled = false;
        RecycleSourceButton.IsEnabled = false;
        PromoteFinalButton.IsEnabled = false;
        CheckFinalizationButton.IsEnabled = false;
        FinalizationStatusText.Text = "Verifying the empty source boundary, promoted final file, and original backup before atomic database completion...";
        try
        {
            FinalizationCompletionResult = await _finalizationCompletionService.CompleteAsync(_recoveryOperation.Id);
            FinalizationStatusText.Text = FinalizationCompletionResult.WasAlreadyCompleted
                ? "The replacement was already completed atomically. The final file and backup remain verified."
                : "Replacement finalisation completed atomically. The final file and verified backup remain available for future undo.";
            FinalizationStatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        catch (Exception exception)
        {
            FinalizationCompletionFailure = exception;
            FinalizationStatusText.Text =
                $"Finalisation completion stopped safely: {exception.Message} Recovery review will retain this record.";
            FinalizationStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
        finally
        {
            _completionInProgress = false;
            await RefreshRecoveryStateAsync(updateCopyStatus: false);
        }
    }

    private async void BeginUndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recoveryOperation is null || !BeginUndoButton.IsEnabled || _undoInProgress)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            "Prepare undo for this completed replacement?\n\n" +
            $"Restore source to: {_recoveryOperation.SourcePath}\n" +
            $"Verified backup: {_recoveryOperation.BackupPath}\n" +
            $"Promoted final: {_recoveryOperation.FinalPath}\n\n" +
            "This step only verifies the completed artifacts and records undo preparation. " +
            "It does not move any file. The source must be restored and verified before the promoted final can be recycled.",
            "Prepare replacement undo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetUndoBusy("Verifying the completed replacement and preparing undo...");
        try
        {
            UndoPreparationResult = await _undoPreparationService.PrepareAsync(_recoveryOperation.Id);
            UndoFailure = null;
        }
        catch (Exception exception)
        {
            UndoFailure = exception;
        }
        finally
        {
            await FinishUndoActionAsync();
        }
    }

    private async void RestoreSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recoveryOperation is null || !RestoreSourceButton.IsEnabled || _undoInProgress)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            "Restore and verify the original source from its backup?\n\n" +
            $"Backup: {_recoveryOperation.BackupPath}\n" +
            $"Restore to: {_recoveryOperation.SourcePath}\n\n" +
            "The source path must be empty and will never be overwritten. " +
            "The promoted final and backup remain unchanged. A matching partial restore can resume safely.",
            "Restore original source",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetUndoBusy("Restoring and SHA-256 verifying the original source...");
        try
        {
            SourceRestorationResult = await _sourceRestorationService.RestoreAsync(_recoveryOperation.Id);
            UndoFailure = null;
        }
        catch (Exception exception)
        {
            UndoFailure = exception;
        }
        finally
        {
            await FinishUndoActionAsync();
        }
    }

    private async void RecycleFinalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recoveryOperation is null || !RecycleFinalButton.IsEnabled || _undoInProgress)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            "Move the promoted final file to the Windows Recycle Bin?\n\n" +
            $"Promoted final: {_recoveryOperation.FinalPath}\n" +
            $"Restored source: {_recoveryOperation.SourcePath}\n" +
            $"Verified backup: {_recoveryOperation.BackupPath}\n\n" +
            "The restored source and backup will be re-verified and left unchanged. " +
            "If Windows cannot guarantee a Recycle Bin operation, this action fails instead of deleting permanently.",
            "Recycle promoted final file",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetUndoBusy("Revalidating the restored source and backup before recording promoted-final recycling intent...");
        try
        {
            FinalFileRecycleResult = await _finalFileRecycleService.RecycleAsync(_recoveryOperation.Id);
            UndoFailure = null;
        }
        catch (Exception exception)
        {
            UndoFailure = exception;
        }
        finally
        {
            await FinishUndoActionAsync();
        }
    }

    private async void CompleteUndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recoveryOperation is null || !CompleteUndoButton.IsEnabled || _undoInProgress)
        {
            return;
        }

        SetUndoBusy("Verifying the restored source and backup before atomic undo completion...");
        try
        {
            UndoCompletionResult = await _undoCompletionService.CompleteAsync(_recoveryOperation.Id);
            UndoFailure = null;
        }
        catch (Exception exception)
        {
            UndoFailure = exception;
        }
        finally
        {
            await FinishUndoActionAsync();
        }
    }

    private async Task FinishUndoActionAsync()
    {
        _undoInProgress = false;
        await RefreshRecoveryStateAsync(updateCopyStatus: false);
        if (UndoFailure is not null)
        {
            FinalizationStatusText.Text = $"Undo stopped safely: {UndoFailure.Message} Recovery review retained the current checkpoint.";
            FinalizationStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
    }

    private void SetUndoBusy(string status)
    {
        _undoInProgress = true;
        UndoPreparationResult = null;
        SourceRestorationResult = null;
        FinalFileRecycleResult = null;
        UndoCompletionResult = null;
        UndoFailure = null;
        BeginUndoButton.IsEnabled = false;
        RestoreSourceButton.IsEnabled = false;
        RecycleFinalButton.IsEnabled = false;
        CompleteUndoButton.IsEnabled = false;
        CompleteFinalizationButton.IsEnabled = false;
        RecycleSourceButton.IsEnabled = false;
        PromoteFinalButton.IsEnabled = false;
        CheckFinalizationButton.IsEnabled = false;
        FinalizationStatusText.Text = status;
        FinalizationStatusText.Foreground = System.Windows.Media.Brushes.Black;
    }

    private void CancelCopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_copyInProgress || _copyCancellation is null)
        {
            return;
        }

        CancelCopyButton.IsEnabled = false;
        CopyStatusText.Text = "Cancellation requested. The current write will stop safely...";
        _copyCancellation.Cancel();
    }

    private void ReplacementReviewWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_copyInProgress && !_backupInProgress && !_promotionInProgress && !_sourceRecycleInProgress && !_completionInProgress && !_undoInProgress && !_oneClickInProgress)
        {
            return;
        }

        if (_promotionInProgress || _sourceRecycleInProgress || _completionInProgress || _undoInProgress || _oneClickInProgress)
        {
            e.Cancel = true;
            System.Windows.MessageBox.Show(
                this,
                _oneClickInProgress
                    ? "The confirmed replacement workflow is running and cannot be cancelled after it begins. Wait for it to finish or stop safely before closing."
                    : _undoInProgress
                    ? "The undo transaction is being verified and cannot be cancelled safely. Wait for it to finish before closing."
                    : _completionInProgress
                    ? "Finalisation completion is being recorded and cannot be cancelled safely. Wait for it to finish before closing."
                    : _sourceRecycleInProgress
                        ? "Source recycling is being verified and cannot be cancelled safely. Wait for it to finish before closing."
                        : "Atomic promotion is being verified and cannot be cancelled safely. Wait for it to finish before closing.",
                _oneClickInProgress
                    ? "Replacement in progress"
                    : _undoInProgress
                    ? "Undo operation in progress"
                    : _completionInProgress
                    ? "Finalisation completion in progress"
                    : _sourceRecycleInProgress ? "Source recycling in progress" : "Atomic promotion in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            _backupInProgress
                ? "An original-backup copy is in progress. Cancel it and close after the operation stops? Any partial backup will be retained."
                : "A temporary copy is in progress. Cancel it and close after the operation stops? Any partial temporary file will be retained.",
            "File operation in progress",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        e.Cancel = true;
        if (confirmation == MessageBoxResult.Yes)
        {
            _closeWhenFinished = true;
            if (_backupInProgress)
            {
                CancelBackupButton_Click(this, new RoutedEventArgs());
            }
            else
            {
                CancelCopyButton_Click(this, new RoutedEventArgs());
            }
        }
    }

    private void ShowRecoveryState(ReplacementRecoveryReview review)
    {
        RecoveryStatusText.Text = review.Message;
        RecoveryStatusText.Visibility = Visibility.Visible;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "Unavailable";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes.Value;
        var unit = 0;
        while (Math.Abs(value) >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
