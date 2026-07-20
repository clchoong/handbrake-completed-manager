using System.Windows;

namespace HandBrakeCompletedManager.App;

public partial class BulkConfirmationWindow : Window
{
    public BulkConfirmationWindow(
        string title,
        string heading,
        string description,
        string actionLabel,
        IReadOnlyList<BulkConfirmationItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        InitializeComponent();
        Title = title;
        HeadingText.Text = heading;
        DescriptionText.Text = description;
        ConfirmButton.Content = actionLabel;
        Items = items;
        DataContext = this;

        var eligibleCount = items.Count(item => item.CanProceed);
        var blockedCount = items.Count - eligibleCount;
        SummaryText.Text = blockedCount == 0
            ? $"All {eligibleCount:N0} selected item(s) passed the initial review. Every path is listed below."
            : $"{eligibleCount:N0} item(s) can proceed; {blockedCount:N0} are blocked and will be skipped. Every path and initial status is listed below.";
        ConfirmButton.IsEnabled = eligibleCount > 0;
    }

    public IReadOnlyList<BulkConfirmationItem> Items { get; }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

public sealed record BulkConfirmationItem(
    string SourcePath,
    string TargetPath,
    string Status,
    bool CanProceed);
