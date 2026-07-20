using System.Windows;

namespace HandBrakeCompletedManager.App;

public partial class FileActionConfirmationWindow : Window
{
    public FileActionConfirmationWindow(
        string title,
        string heading,
        string description,
        string sourcePath,
        string outputPath,
        string safetyText,
        string actionLabel)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = heading;
        DescriptionText.Text = description;
        SourcePathTextBox.Text = sourcePath;
        OutputPathTextBox.Text = outputPath;
        SafetyText.Text = safetyText;
        ConfirmButton.Content = actionLabel;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
