using System.Windows;

namespace HandBrakeCompletedManager.App;

public enum DirectReplacementChoice
{
    ReplaceSource,
    ReplaceSourceAndKeepOutput
}

public partial class DirectReplacementConfirmationWindow : Window
{
    public DirectReplacementConfirmationWindow(string sourcePath, string outputPath, string replacementPath)
    {
        InitializeComponent();
        PopupWindowRendering.Stabilize(this);
        SourcePathTextBox.Text = sourcePath;
        OutputPathTextBox.Text = outputPath;
        ReplacementPathTextBox.Text = replacementPath;
    }

    public DirectReplacementChoice Choice { get; private set; }

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = DirectReplacementChoice.ReplaceSource;
        DialogResult = true;
    }

    private void KeepOutputButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = DirectReplacementChoice.ReplaceSourceAndKeepOutput;
        DialogResult = true;
    }
}
