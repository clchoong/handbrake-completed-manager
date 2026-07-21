using System.IO;
using System.Windows;
using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.App;

public partial class SettingsWindow : Window
{
    private readonly WindowsFileActionService _fileActions;
    private readonly string _logDirectory;

    public SettingsWindow(
        ApplicationSettings settings,
        WindowsFileActionService fileActions,
        string logDirectory)
    {
        InitializeComponent();
        PopupWindowRendering.Stabilize(this);
        _fileActions = fileActions;
        _logDirectory = Path.GetFullPath(logDirectory);
        SavedSettings = settings.Normalize();

        StartMinimizedCheckBox.IsChecked = SavedSettings.StartMinimized;
        CloseToTrayCheckBox.IsChecked = SavedSettings.CloseToTray;
        NotificationsEnabledCheckBox.IsChecked = SavedSettings.NotificationsEnabled;
        LogDirectoryTextBox.Text = _logDirectory;
        RefreshIntervalComboBox.SelectedItem = RefreshIntervalComboBox.Items
            .Cast<System.Windows.Controls.ComboBoxItem>()
            .First(item => (string)item.Tag == SavedSettings.HistoryRefreshSeconds.ToString());
    }

    public ApplicationSettings SavedSettings { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedInterval =
            (RefreshIntervalComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        var refreshSeconds = int.TryParse(selectedInterval, out var parsed)
            ? parsed
            : ApplicationSettings.Default.HistoryRefreshSeconds;

        SavedSettings = new ApplicationSettings(
            StartMinimizedCheckBox.IsChecked == true,
            CloseToTrayCheckBox.IsChecked == true,
            NotificationsEnabledCheckBox.IsChecked == true,
            refreshSeconds).Normalize();
        DialogResult = true;
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            SettingsStatusText.Text = _fileActions.OpenFolder(_logDirectory).Message;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SettingsStatusText.Text = $"Unable to open the log folder: {exception.Message}";
        }
    }
}
