using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;
using Microsoft.Win32;

namespace HandBrakeCompletedManager.App;

public partial class MainWindow : Window
{
    private readonly string _databasePath = StoragePaths.ResolveDatabasePath();
    private readonly CompletedEncodeRepository _repository;
    private readonly HandBrakeDetector _handBrakeDetector = new();
    private readonly HandBrakeConnectionTester _connectionTester = new();
    private readonly WindowsFileActionService _fileActions = new();
    private readonly HandBrakeConnectionStore _connectionStore;
    private readonly DispatcherTimer _historyRefreshTimer;
    private bool _isLoadingHistory;

    public MainWindow()
    {
        InitializeComponent();
        var completionSetup = HandBrakeCompletionSetup.Create(
            Path.Combine(AppContext.BaseDirectory, "HandBrakeCompletedManager.Receiver.exe"));
        ReceiverPathTextBox.Text = completionSetup.ReceiverPath;
        ReceiverArgumentsTextBox.Text = completionSetup.Arguments;
        ReceiverStatusText.Text = completionSetup.ReceiverExists
            ? "Receiver ready - copy these values into HandBrake"
            : "Receiver is missing - rebuild the desktop solution before setup";
        _repository = new CompletedEncodeRepository(_databasePath);
        _connectionStore = new HandBrakeConnectionStore(StoragePaths.ResolveConnectionsPath());
        _historyRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _historyRefreshTimer.Tick += HistoryRefreshTimer_Tick;
        DataContext = this;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    public ObservableCollection<HistoryRow> HistoryRows { get; } = [];
    public ObservableCollection<HandBrakeInstallation> DetectedInstallations { get; } = [];

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadHistoryAsync();
        await FindHandBrakeAsync();
        _historyRefreshTimer.Start();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadHistoryAsync();
    }

    private async void HistoryRefreshTimer_Tick(object? sender, EventArgs e)
    {
        await LoadHistoryAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _historyRefreshTimer.Stop();
    }

    private async void FindHandBrakeButton_Click(object sender, RoutedEventArgs e)
    {
        await FindHandBrakeAsync();
    }

    private async void BrowseHandBrakeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select HandBrake.exe",
            Filter = "HandBrake executable (HandBrake.exe)|HandBrake.exe|Executable files (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await FindHandBrakeAsync([dialog.FileName]);
        }
    }

    private void InstallationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TestConnectionButton.IsEnabled =
            InstallationComboBox.SelectedItem is HandBrakeInstallation { Exists: true };
        _ = UpdateConnectionStatusAsync();
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (InstallationComboBox.SelectedItem is not HandBrakeInstallation installation)
        {
            ConnectionStatusText.Text = "Select a HandBrake installation first";
            return;
        }

        try
        {
            SetConnectionControlsEnabled(false);
            ConnectionStatusText.Text = "Testing connection...";
            var result = await _connectionTester.TestAsync(installation);

            if (result.IsSuccess)
            {
                await _connectionStore.SaveConnectedAsync(installation.ExecutablePath, DateTimeOffset.UtcNow);
            }

            ConnectionStatusText.Text = result.IsSuccess ? "Pipeline test passed" : "Pipeline test failed";
            StatusText.Text = result.Message;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ConnectionStatusText.Text = "Test failed";
            StatusText.Text = $"Unable to save connection settings: {exception.Message}";
        }
        finally
        {
            SetConnectionControlsEnabled(true);
        }
    }

    private async Task FindHandBrakeAsync(IEnumerable<string>? userLocations = null)
    {
        try
        {
            SetConnectionControlsEnabled(false);
            ConnectionStatusText.Text = "Searching...";
            var previouslySelectedPath =
                (InstallationComboBox.SelectedItem as HandBrakeInstallation)?.ExecutablePath;
            var connections = await _connectionStore.LoadAsync();
            var searchLocations = (userLocations ?? [])
                .Concat(connections.Select(connection => connection.ExecutablePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var installations = (await _handBrakeDetector.DetectAsync(searchLocations)).ToList();

            foreach (var connection in connections.Where(connection =>
                         installations.All(installation =>
                             !installation.ExecutablePath.Equals(
                                 connection.ExecutablePath,
                                 StringComparison.OrdinalIgnoreCase))))
            {
                installations.Add(new HandBrakeInstallation(
                    connection.ExecutablePath,
                    null,
                    HandBrakeInstallationType.Unknown,
                    Exists: false,
                    IsRunning: false,
                    "Saved connection"));
            }

            DetectedInstallations.Clear();
            foreach (var installation in installations)
            {
                DetectedInstallations.Add(installation);
            }

            InstallationComboBox.SelectedItem = installations.FirstOrDefault(installation =>
                    installation.ExecutablePath.Equals(previouslySelectedPath, StringComparison.OrdinalIgnoreCase))
                ?? installations.FirstOrDefault(installation => connections.Any(connection =>
                    connection.IsConnected &&
                    connection.ExecutablePath.Equals(installation.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                ?? installations.FirstOrDefault();

            ConnectionStatusText.Text = installations.Count == 0
                ? "No HandBrake installation found"
                : $"{installations.Count} installation(s) found";
            await UpdateConnectionStatusAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ConnectionStatusText.Text = "Detection failed";
            StatusText.Text = $"Unable to read connection settings: {exception.Message}";
        }
        finally
        {
            SetConnectionControlsEnabled(true);
        }
    }

    private async Task UpdateConnectionStatusAsync()
    {
        if (InstallationComboBox.SelectedItem is not HandBrakeInstallation installation)
        {
            TestConnectionButton.IsEnabled = false;
            return;
        }

        try
        {
            var connections = await _connectionStore.LoadAsync();
            var connection = connections.FirstOrDefault(item =>
                item.ExecutablePath.Equals(installation.ExecutablePath, StringComparison.OrdinalIgnoreCase));

            ConnectionStatusText.Text = !installation.Exists
                ? "Saved HandBrake location is missing"
                : connection?.IsConnected == true
                    ? $"Pipeline tested {connection.LastTestedUtc?.ToLocalTime():g}"
                    : "Detected - pipeline not tested";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ConnectionStatusText.Text = $"Settings unavailable: {exception.Message}";
        }
    }

    private void SetConnectionControlsEnabled(bool isEnabled)
    {
        FindHandBrakeButton.IsEnabled = isEnabled;
        BrowseHandBrakeButton.IsEnabled = isEnabled;
        InstallationComboBox.IsEnabled = isEnabled;
            TestConnectionButton.IsEnabled = isEnabled &&
            InstallationComboBox.SelectedItem is HandBrakeInstallation { Exists: true };
    }

    private void CopyReceiverPathButton_Click(object sender, RoutedEventArgs e)
    {
        CopySetupValue(ReceiverPathTextBox.Text, "Receiver executable path copied.");
    }

    private void CopyReceiverArgumentsButton_Click(object sender, RoutedEventArgs e)
    {
        CopySetupValue(ReceiverArgumentsTextBox.Text, "HandBrake arguments copied.");
    }

    private void CopySetupValue(string value, string successMessage)
    {
        try
        {
            Clipboard.SetText(value);
            StatusText.Text = successMessage;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException)
        {
            StatusText.Text = $"Unable to access the clipboard: {exception.Message}";
        }
    }

    private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = HistoryGrid.SelectedItem as HistoryRow;
        var hasSelection = row is not null;

        PlayDestinationButton.IsEnabled = hasSelection;
        PlaySourceButton.IsEnabled = hasSelection;
        RevealDestinationButton.IsEnabled = hasSelection;
        RevealSourceButton.IsEnabled = hasSelection;
        CopyDestinationPathButton.IsEnabled = hasSelection;
        CopySourcePathButton.IsEnabled = hasSelection;
        SelectedRecordText.Text = row is null
            ? "Select a completed encode"
            : $"Selected: {row.DestinationFilename}";
    }

    private void HistoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(HistoryGrid, source) is not DataGridRow row ||
            row.Item is not HistoryRow historyRow)
        {
            return;
        }

        RunFileAction(_fileActions.Play, historyRow.DestinationPath);
        e.Handled = true;
    }

    private void PlayDestinationButton_Click(object sender, RoutedEventArgs e) =>
        RunSelectedFileAction(_fileActions.Play, row => row.DestinationPath);

    private void PlaySourceButton_Click(object sender, RoutedEventArgs e) =>
        RunSelectedFileAction(_fileActions.Play, row => row.SourcePath);

    private void RevealDestinationButton_Click(object sender, RoutedEventArgs e) =>
        RunSelectedFileAction(_fileActions.Reveal, row => row.DestinationPath);

    private void RevealSourceButton_Click(object sender, RoutedEventArgs e) =>
        RunSelectedFileAction(_fileActions.Reveal, row => row.SourcePath);

    private void CopyDestinationPathButton_Click(object sender, RoutedEventArgs e) =>
        CopySelectedPath(row => row.DestinationPath, "Converted-file path copied.");

    private void CopySourcePathButton_Click(object sender, RoutedEventArgs e) =>
        CopySelectedPath(row => row.SourcePath, "Source-file path copied.");

    private void RunSelectedFileAction(
        Func<string, FileActionResult> action,
        Func<HistoryRow, string> selectPath)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row)
        {
            StatusText.Text = "Select a completed encode first.";
            return;
        }

        RunFileAction(action, selectPath(row));
    }

    private void RunFileAction(Func<string, FileActionResult> action, string path)
    {
        var result = action(path);
        StatusText.Text = result.Message;
    }

    private void CopySelectedPath(Func<HistoryRow, string> selectPath, string successMessage)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row)
        {
            StatusText.Text = "Select a completed encode first.";
            return;
        }

        CopySetupValue(selectPath(row), successMessage);
    }

    private async Task LoadHistoryAsync()
    {
        if (_isLoadingHistory)
        {
            return;
        }

        try
        {
            _isLoadingHistory = true;
            RefreshButton.IsEnabled = false;
            StatusText.Text = "Loading completed history...";
            await _repository.InitializeAsync();
            var records = await _repository.GetAllAsync();
            var selectedRecordId = (HistoryGrid.SelectedItem as HistoryRow)?.Id;

            HistoryRows.Clear();
            foreach (var record in records)
            {
                HistoryRows.Add(HistoryRow.From(record));
            }

            HistoryGrid.SelectedItem = selectedRecordId is null
                ? null
                : HistoryRows.FirstOrDefault(row => row.Id == selectedRecordId);

            CompletedCountText.Text = records.Count.ToString("N0");
            OriginalSizeText.Text = FormatBytes(records.Sum(item => item.SourceSize ?? 0));
            ConvertedSizeText.Text = FormatBytes(records.Sum(item => item.DestinationSize ?? 0));
            SpaceSavedText.Text = FormatBytes(records.Sum(item => item.SpaceSavedBytes ?? 0));
            StatusText.Text = $"{records.Count:N0} record(s) - {_databasePath}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Unable to load history: {exception.Message}";
        }
        finally
        {
            _isLoadingHistory = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var size = (double)bytes;
        var unitIndex = 0;

        while (Math.Abs(size) >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }
}

public sealed record HistoryRow(
    Guid Id,
    string SourcePath,
    string DestinationPath,
    string Completed,
    string Status,
    string SourceFilename,
    string SourceSize,
    string DestinationSize,
    string OutputPercentage,
    string SpaceSaved,
    string DestinationFilename)
{
    public static HistoryRow From(CompletedEncode item) => new(
        item.Id,
        item.SourcePath,
        item.DestinationPath,
        item.CompletedAtUtc.ToLocalTime().ToString("g"),
        item.CurrentStatus,
        item.SourceFilename,
        FormatNullableBytes(item.SourceSize),
        FormatNullableBytes(item.DestinationSize),
        item.OutputPercentage is null ? "-" : $"{item.OutputPercentage:0.##}%",
        FormatNullableBytes(item.SpaceSavedBytes),
        item.DestinationFilename);

    private static string FormatNullableBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "Missing";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var size = (double)bytes.Value;
        var unitIndex = 0;

        while (Math.Abs(size) >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }
}
