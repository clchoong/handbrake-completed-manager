using System.ComponentModel;
using System.IO;
using System.Windows;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using MediaBrushes = System.Windows.Media.Brushes;

namespace HandBrakeCompletedManager.App;

public partial class ReplacementProgressWindow : Window
{
    private readonly ReplacementPlan _plan;
    private readonly SafeReplacementService _service;
    private bool _isRunning = true;

    public ReplacementProgressWindow(ReplacementPlan plan, SafeReplacementService service)
    {
        InitializeComponent();
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        FileNameText.Text = Path.GetFileName(plan.CompletedEncode.SourcePath);
        Loaded += ReplacementProgressWindow_Loaded;
        Closing += ReplacementProgressWindow_Closing;
    }

    public SafeReplacementResult? Result { get; private set; }
    public Exception? Failure { get; private set; }

    private async void ReplacementProgressWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ReplacementProgressWindow_Loaded;
        var progress = new Progress<SafeReplacementProgress>(UpdateProgress);
        try
        {
            Result = await _service.ReplaceAsync(_plan, progress);
            OverallProgressBar.IsIndeterminate = false;
            OverallProgressBar.Value = 100;
            StageText.Text = "Replacement complete";
            ProgressText.Text = "Verified converted file installed beside the original location";
            PercentageText.Text = "100%";
            OutcomeText.Text =
                "Completed safely. The original source is in the Windows Recycle Bin and a verified backup remains available for Undo.";
            OutcomeText.Foreground = MediaBrushes.DarkGreen;
        }
        catch (Exception exception)
        {
            Failure = exception;
            OverallProgressBar.IsIndeterminate = false;
            StageText.Text = "Replacement stopped safely";
            ProgressText.Text = "No further stages will run";
            PercentageText.Text = string.Empty;
            OutcomeText.Text = $"{exception.Message} Use Recovery to inspect or continue the recorded checkpoint.";
            OutcomeText.Foreground = MediaBrushes.DarkRed;
        }
        finally
        {
            _isRunning = false;
            CloseButton.IsEnabled = true;
        }
    }

    private void UpdateProgress(SafeReplacementProgress progress)
    {
        var (start, span, stageLabel) = progress.Stage switch
        {
            SafeReplacementStage.CopyingConvertedFile => (0d, 45d, "Copying converted file"),
            SafeReplacementStage.BackingUpOriginalSource => (45d, 38d, "Protecting original source"),
            SafeReplacementStage.VerifyingAllArtifacts => (84d, 3d, "Verifying files"),
            SafeReplacementStage.PromotingConvertedFile => (88d, 4d, "Installing converted file"),
            SafeReplacementStage.RecyclingOriginalSource => (93d, 4d, "Moving original to Recycle Bin"),
            SafeReplacementStage.CompletingTransaction => (98d, 1d, "Finishing replacement"),
            _ => (0d, 0d, "Replacing source")
        };

        StageText.Text = stageLabel;
        ProgressText.Text = progress.Message;
        if (progress.Percentage is double stagePercentage)
        {
            var overall = Math.Clamp(start + (stagePercentage / 100d * span), 0, 99);
            OverallProgressBar.IsIndeterminate = false;
            OverallProgressBar.Value = overall;
            PercentageText.Text = $"{overall:0}%";
        }
        else
        {
            OverallProgressBar.IsIndeterminate = true;
            PercentageText.Text = string.Empty;
        }
    }

    private void ReplacementProgressWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isRunning)
        {
            e.Cancel = true;
            OutcomeText.Text = "Replacement is still running. This window will become closable at the next safe completion or recovery point.";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
