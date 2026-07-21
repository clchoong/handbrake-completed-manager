using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using MediaBrushes = System.Windows.Media.Brushes;

namespace HandBrakeCompletedManager.App;

public partial class ReplacementProgressWindow : Window
{
    private readonly CompletedEncode _record;
    private readonly bool _keepOutput;
    private readonly DirectSourceReplacementService _service;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _isRunning = true;

    public ReplacementProgressWindow(
        CompletedEncode record,
        bool keepOutput,
        DirectSourceReplacementService service)
    {
        InitializeComponent();
        PopupWindowRendering.Stabilize(this);
        _record = record;
        _keepOutput = keepOutput;
        _service = service;
        FileNameText.Text = Path.GetFileName(record.SourcePath);
        ContentRendered += ReplacementProgressWindow_ContentRendered;
        Closing += ReplacementProgressWindow_Closing;
    }

    public DirectReplacementResult? Result { get; private set; }
    public Exception? Failure { get; private set; }
    public bool WasCancelled { get; private set; }

    private async void ReplacementProgressWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= ReplacementProgressWindow_ContentRendered;
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        var progress = new Progress<DirectReplacementProgress>(UpdateProgress);
        try
        {
            Result = await _service.ReplaceAsync(_record, _keepOutput, progress, _cancellation.Token);
            OverallProgressBar.IsIndeterminate = false;
            OverallProgressBar.Value = 100;
            StageText.Text = "Replacement complete";
            ProgressText.Text = Result.OutputKept ? "The separate output was kept" : "The output was moved into the source library";
            PercentageText.Text = "100%";
            OutcomeText.Text = Result.OutputKept
                ? "Source replaced. The original cannot be recovered, and the separate output was kept."
                : "Source replaced. The original cannot be recovered, and no separate output remains.";
            OutcomeText.Foreground = MediaBrushes.DarkGreen;
        }
        catch (OperationCanceledException)
        {
            WasCancelled = true;
            OverallProgressBar.IsIndeterminate = false;
            StageText.Text = "Replacement cancelled";
            ProgressText.Text = "The original source and output were not changed";
            PercentageText.Text = string.Empty;
            OutcomeText.Text = "You can close this window and try Replace Source again.";
        }
        catch (Exception exception)
        {
            Failure = exception;
            OverallProgressBar.IsIndeterminate = false;
            StageText.Text = "Replacement stopped";
            ProgressText.Text = "No further steps will run";
            PercentageText.Text = string.Empty;
            OutcomeText.Text = $"{exception.Message} Close this window and try again.";
            OutcomeText.Foreground = MediaBrushes.DarkRed;
        }
        finally
        {
            _isRunning = false;
            CancelButton.IsEnabled = false;
            CloseButton.IsEnabled = true;
        }
    }

    private void UpdateProgress(DirectReplacementProgress progress)
    {
        StageText.Text = progress.Stage switch
        {
            DirectReplacementStage.Preparing => "Preparing replacement",
            DirectReplacementStage.Transferring => "Transferring output",
            DirectReplacementStage.Installing => "Replacing source",
            DirectReplacementStage.DeletingOutput => "Removing separate output",
            DirectReplacementStage.Completed => "Replacement complete",
            _ => "Replacing source"
        };
        ProgressText.Text = progress.Message;
        CancelButton.IsEnabled = progress.CanCancel;
        OverallProgressBar.IsIndeterminate = progress.TotalBytes <= 0;
        if (!OverallProgressBar.IsIndeterminate)
        {
            OverallProgressBar.Value = progress.Percentage;
            PercentageText.Text = $"{progress.Percentage:0}%";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        ProgressText.Text = "Cancelling after the current transfer operation...";
        _cancellation.Cancel();
    }

    private void ReplacementProgressWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isRunning)
        {
            e.Cancel = true;
            OutcomeText.Text = "Use Cancel while a copy is in progress. The window can close after the operation reaches a safe boundary.";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
