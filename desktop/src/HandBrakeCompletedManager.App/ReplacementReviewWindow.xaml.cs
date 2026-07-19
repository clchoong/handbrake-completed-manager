using System.Windows;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.App;

public partial class ReplacementReviewWindow : Window
{
    public ReplacementReviewWindow(ReplacementPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        InitializeComponent();
        SourcePathTextBox.Text = plan.CompletedEncode.SourcePath;
        DestinationPathTextBox.Text = plan.CompletedEncode.DestinationPath;
        FinalPathTextBox.Text = plan.Paths.FinalPath;
        TemporaryPathTextBox.Text = plan.Paths.TemporaryPath;
        BackupPathTextBox.Text = plan.Paths.BackupPath;
        SizesText.Text = $"Source {FormatBytes(plan.Snapshot.SourceSize)}  |  " +
                         $"Converted {FormatBytes(plan.Snapshot.DestinationSize)}";
        OutcomeText.Text = plan.CanProceed
            ? "Preflight checks passed. File replacement remains disabled until copy, verification, backup, undo, and recovery execution are implemented."
            : "Replacement is blocked. Resolve every blocking item before a future replacement operation can begin.";
        OutcomeText.Foreground = plan.CanProceed
            ? System.Windows.Media.Brushes.DarkGreen
            : System.Windows.Media.Brushes.DarkRed;
        IssuesList.ItemsSource = plan.Issues.Count == 0
            ? ["No blocking issues or warnings were found."]
            : plan.Issues.Select(issue =>
                $"{(issue.Severity == ReplacementIssueSeverity.Blocking ? "BLOCKING" : "WARNING")}: {issue.Message}")
                .ToArray();
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
