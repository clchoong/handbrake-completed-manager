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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
