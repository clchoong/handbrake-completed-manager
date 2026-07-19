using System.Windows;

namespace HandBrakeCompletedManager.App;

public partial class App : System.Windows.Application
{
    private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (MainWindow is HandBrakeCompletedManager.App.MainWindow window)
        {
            window.PrepareForShutdown();
        }
    }
}

