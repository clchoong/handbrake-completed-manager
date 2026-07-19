using System.ComponentModel;
using System.IO;
using System.Windows;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.App;

public partial class ReplacementReviewWindow : Window
{
    private readonly ReplacementPlan _plan;
    private readonly TemporaryCopyService _temporaryCopyService;
    private readonly ReplacementOperationRepository _operationRepository;
    private CancellationTokenSource? _copyCancellation;
    private bool _copyInProgress;
    private bool _closeWhenFinished;

    public ReplacementReviewWindow(
        ReplacementPlan plan,
        TemporaryCopyService temporaryCopyService,
        ReplacementOperationRepository operationRepository)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(temporaryCopyService);
        ArgumentNullException.ThrowIfNull(operationRepository);
        _plan = plan;
        _temporaryCopyService = temporaryCopyService;
        _operationRepository = operationRepository;
        InitializeComponent();
        SourcePathTextBox.Text = plan.CompletedEncode.SourcePath;
        DestinationPathTextBox.Text = plan.CompletedEncode.DestinationPath;
        FinalPathTextBox.Text = plan.Paths.FinalPath;
        TemporaryPathTextBox.Text = plan.Paths.TemporaryPath;
        BackupPathTextBox.Text = plan.Paths.BackupPath;
        SizesText.Text = $"Source {FormatBytes(plan.Snapshot.SourceSize)}  |  " +
                         $"Converted {FormatBytes(plan.Snapshot.DestinationSize)}";
        OutcomeText.Text = plan.CanProceed
            ? "Preflight checks passed. A separate temporary copy may be created and verified. Source backup and replacement remain disabled."
            : "Preparation is blocked. Resolve every blocking item before creating a temporary copy.";
        OutcomeText.Foreground = plan.CanProceed
            ? System.Windows.Media.Brushes.DarkGreen
            : System.Windows.Media.Brushes.DarkRed;
        IssuesList.ItemsSource = plan.Issues.Count == 0
            ? ["No blocking issues or warnings were found."]
            : plan.Issues.Select(issue =>
                $"{(issue.Severity == ReplacementIssueSeverity.Blocking ? "BLOCKING" : "WARNING")}: {issue.Message}")
                .ToArray();
        PrepareCopyButton.IsEnabled = false;
        CopyStatusText.Text = "Checking previous operation state...";
        Loaded += ReplacementReviewWindow_Loaded;
        Closing += ReplacementReviewWindow_Closing;
    }

    public TemporaryCopyResult? CopyResult { get; private set; }
    public bool CopyWasCancelled { get; private set; }
    public Exception? CopyFailure { get; private set; }

    private async void ReplacementReviewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var canPrepare = _plan.CanProceed;
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
                    ShowRecoveryState(recoveryReview);
                    canPrepare &= !recoveryReview.BlocksNewCopy;
                }
            }

            PrepareCopyButton.IsEnabled = canPrepare;
            CopyStatusText.Text = canPrepare
                ? "Ready to create a verified temporary copy."
                : "Temporary-copy preparation is not available until the displayed safety or recovery issue is resolved.";
        }
        catch (Exception exception)
        {
            PrepareCopyButton.IsEnabled = false;
            CopyStatusText.Text = "Temporary-copy preparation is disabled because recovery state could not be checked.";
            RecoveryStatusText.Text = $"Existing operation state could not be read: {exception.Message}";
            RecoveryStatusText.Visibility = Visibility.Visible;
        }
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
        CopyWasCancelled = false;
        CopyFailure = null;
        PrepareCopyButton.IsEnabled = false;
        CancelCopyButton.IsEnabled = true;
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
            _copyCancellation.Dispose();
            _copyCancellation = null;
            if (_closeWhenFinished)
            {
                Close();
            }
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
        if (!_copyInProgress)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            "A temporary copy is in progress. Cancel it and close after the operation stops? " +
            "Any partial temporary file will be retained for recovery review.",
            "Copy in progress",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        e.Cancel = true;
        if (confirmation == MessageBoxResult.Yes)
        {
            _closeWhenFinished = true;
            CancelCopyButton_Click(this, new RoutedEventArgs());
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
