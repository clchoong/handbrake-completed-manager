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
        SourceRecycleService sourceRecycleService)
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
        InitializeComponent();
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
            ? "Preflight checks passed. A temporary copy may be verified first, followed by a separate original-backup copy. Source movement and replacement remain disabled."
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
                    DiscardTemporaryButton.IsEnabled = false;
                }
                ApplyBackupState(operation, _backupState, _finalizationTransaction);
            }

            PrepareCopyButton.IsEnabled = canPrepare && !_copyInProgress && !_backupInProgress;
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
            !_sourceRecycleInProgress;
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
            !_sourceRecycleInProgress;
        RecycleSourceButton.Content = finalizationTransaction?.Checkpoint == FinalizationCheckpoint.RecycleSourceIntentRecorded
            ? "Recover source recycle"
            : "Recycle original source";
        FinalizationStatusText.Foreground = finalizationTransaction?.Checkpoint switch
        {
            FinalizationCheckpoint.FinalPromoted or FinalizationCheckpoint.SourceRecycled => System.Windows.Media.Brushes.DarkGreen,
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
                "Original source moved to the Windows Recycle Bin. The verified backup and promoted final file remain untouched.",
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
                "The verified temporary copy is retained. Source backup and final replacement are still disabled.";
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
        if (!_copyInProgress && !_backupInProgress && !_promotionInProgress && !_sourceRecycleInProgress)
        {
            return;
        }

        if (_promotionInProgress || _sourceRecycleInProgress)
        {
            e.Cancel = true;
            System.Windows.MessageBox.Show(
                this,
                _sourceRecycleInProgress
                    ? "Source recycling is being verified and cannot be cancelled safely. Wait for it to finish before closing."
                    : "Atomic promotion is being verified and cannot be cancelled safely. Wait for it to finish before closing.",
                _sourceRecycleInProgress ? "Source recycling in progress" : "Atomic promotion in progress",
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
