using System.Windows;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.App;

public partial class RecoveryOverviewWindow : Window
{
    public RecoveryOverviewWindow(IReadOnlyList<ReplacementRecoveryItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        InitializeComponent();
        DataContext = items;
    }

    public Guid? SelectedCompletedEncodeId { get; private set; }

    private void RecoveryGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        OpenRecordButton.IsEnabled = RecoveryGrid.SelectedItem is ReplacementRecoveryItem;

    private void RecoveryGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RecoveryGrid.SelectedItem is ReplacementRecoveryItem)
        {
            OpenSelectedRecord();
        }
    }

    private void OpenRecordButton_Click(object sender, RoutedEventArgs e) => OpenSelectedRecord();

    private void OpenSelectedRecord()
    {
        if (RecoveryGrid.SelectedItem is not ReplacementRecoveryItem item)
        {
            return;
        }

        SelectedCompletedEncodeId = item.CompletedEncodeId;
        DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
