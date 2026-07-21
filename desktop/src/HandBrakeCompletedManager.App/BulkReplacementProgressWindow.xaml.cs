using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace HandBrakeCompletedManager.App;

public partial class BulkReplacementProgressWindow : Window
{
    private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isRunning = true;
    private int _activeIndex = -1;
    private int _processedItems;

    public BulkReplacementProgressWindow(IReadOnlyList<string> fileNames)
    {
        InitializeComponent();
        PopupWindowRendering.Stabilize(this);
        Items = new ObservableCollection<BulkReplacementProgressItem>(
            fileNames.Select((name, index) => new BulkReplacementProgressItem(index + 1, name)));
        DataContext = this;
        OverallCountText.Text = $"0/{Items.Count:N0}";
        Closing += BulkReplacementProgressWindow_Closing;
        ContentRendered += (_, _) => _rendered.TrySetResult();
    }

    public ObservableCollection<BulkReplacementProgressItem> Items { get; }
    public event EventHandler? StopRequested;
    public event EventHandler? CancelCurrentRequested;

    public Task WaitUntilRenderedAsync() => _rendered.Task;

    public void StartItem(int index)
    {
        _activeIndex = index;
        var item = Items[index];
        item.Percentage = 0;
        item.Status = "Preparing replacement";
        item.StatusBrush = MediaBrushes.DimGray;
        CancelCurrentButton.IsEnabled = false;
        OverallStatusText.Text = $"Replacing {index + 1:N0} of {Items.Count:N0}: {item.FileName}";
    }

    public void ReportItem(int index, DirectReplacementProgress progress)
    {
        var item = Items[index];
        item.Percentage = progress.Percentage;
        item.Status = progress.Message;
        item.StatusBrush = MediaBrushes.DimGray;
        CancelCurrentButton.IsEnabled = index == _activeIndex && progress.CanCancel;
        UpdateOverallProgress(_processedItems + progress.Percentage / 100d);
    }

    public void CompleteItem(int index, string status, bool succeeded, bool cancelled)
    {
        var item = Items[index];
        if (succeeded) item.Percentage = 100;
        item.Status = status;
        item.StatusBrush = succeeded
            ? MediaBrushes.DarkGreen
            : cancelled
                ? MediaBrushes.DarkOrange
                : MediaBrushes.DarkRed;
        CancelCurrentButton.IsEnabled = false;
        _activeIndex = -1;
    }

    public void ReportOverall(int processed)
    {
        _processedItems = processed;
        OverallCountText.Text = $"{processed:N0}/{Items.Count:N0}";
        UpdateOverallProgress(processed);
    }

    private void UpdateOverallProgress(double processedItems)
    {
        var percentage = BulkProgressRules.CalculatePercentage(processedItems, Items.Count);
        OverallProgressBar.Value = percentage;
        OverallPercentageText.Text = $"{percentage:0}%";
    }

    public void MarkRemainingSkipped(int startIndex)
    {
        for (var index = startIndex; index < Items.Count; index++)
        {
            Items[index].Status = "Skipped";
            Items[index].StatusBrush = MediaBrushes.DimGray;
        }
    }

    public void Complete(string summary, bool hasFailures)
    {
        _isRunning = false;
        _activeIndex = -1;
        OverallStatusText.Text = summary;
        OverallStatusText.Foreground = hasFailures ? MediaBrushes.DarkRed : MediaBrushes.DarkGreen;
        CancelCurrentButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        CloseButton.IsEnabled = true;
        Activate();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        StopButton.Content = "Stopping after current...";
        OverallStatusText.Text = "Stop requested. The current item will finish safely.";
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CancelCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCurrentButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        OverallStatusText.Text = "Cancelling the current copy and stopping the batch...";
        CancelCurrentRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BulkReplacementProgressWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isRunning)
        {
            e.Cancel = true;
            OverallStatusText.Text = "The bulk replacement is still running. Use a stop or cancel action first.";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class BulkReplacementProgressItem(int number, string fileName) : INotifyPropertyChanged
{
    private double _percentage;
    private string _status = "Waiting";
    private MediaBrush _statusBrush = MediaBrushes.DimGray;

    public int Number { get; } = number;
    public string FileName { get; } = fileName;
    public double Percentage
    {
        get => _percentage;
        set => SetField(ref _percentage, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public MediaBrush StatusBrush
    {
        get => _statusBrush;
        set => SetField(ref _statusBrush, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
