using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

namespace HandBrakeCompletedManager.App;

public partial class AboutWindow : Window
{
    private const string ProjectUrl = "https://github.com/clchoong/handbrake-completed-manager";

    public AboutWindow()
    {
        InitializeComponent();
        PopupWindowRendering.Stabilize(this);
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString(3) ?? "Unknown"
            : informationalVersion.Split('+')[0];
        VersionText.Text = $"Version {version}";
        RuntimeText.Text = $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.ProcessArchitecture}";
    }

    private void ProjectPageButton_Click(object sender, RoutedEventArgs e) => Open(ProjectUrl);

    private void NoticesButton_Click(object sender, RoutedEventArgs e)
    {
        var licensePath = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        Open(File.Exists(licensePath) ? licensePath : $"{ProjectUrl}/blob/main/LICENSE");
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // About information remains visible even when Windows cannot open the link.
        }
    }
}
