using System.ComponentModel;
using System.Windows;
using MediaBrushes = System.Windows.Media.Brushes;

namespace HandBrakeCompletedManager.App;

public partial class BulkOperationProgressWindow : Window
{
    private bool _isRunning = true;
    private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BulkOperationProgressWindow(string title, string heading, string subtitle)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = heading;
        SubtitleText.Text = subtitle;
        Closing += BulkOperationProgressWindow_Closing;
        ContentRendered += (_, _) => _rendered.TrySetResult();
    }

    public Task WaitUntilRenderedAsync() => _rendered.Task;

    public void Report(int completed, int total, string item)
    {
        var percentage = total == 0 ? 100 : Math.Clamp(completed * 100d / total, 0, 100);
        CurrentItemText.Text = item;
        ProgressBar.Value = percentage;
        StatusText.Text = $"{completed:N0} of {total:N0} processed";
        PercentageText.Text = $"{percentage:0}%";
    }

    public void Complete(string summary, bool hasFailures)
    {
        _isRunning = false;
        ProgressBar.Value = 100;
        CurrentItemText.Text = summary;
        CurrentItemText.Foreground = hasFailures ? MediaBrushes.DarkRed : MediaBrushes.DarkGreen;
        StatusText.Text = "Operation finished";
        PercentageText.Text = "100%";
        CloseButton.IsEnabled = true;
        Activate();
    }

    private void BulkOperationProgressWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isRunning)
        {
            e.Cancel = true;
            StatusText.Text = "The confirmed operation is still running.";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
