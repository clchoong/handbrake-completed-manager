using System.Windows;

using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.App;

public partial class App : System.Windows.Application
{
    private const string ApplicationId = "HandBrakeCompletedManager.App";
    private SingleInstanceCoordinator? _singleInstanceCoordinator;
    private bool _activationPending;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        _singleInstanceCoordinator = new SingleInstanceCoordinator(
            ApplicationId,
            () => Dispatcher.BeginInvoke(ActivateMainWindow));
        if (!_singleInstanceCoordinator.IsPrimaryInstance)
        {
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (_activationPending)
        {
            _activationPending = false;
            window.ActivateFromExternalLaunch();
        }
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is MainWindow window)
        {
            window.ActivateFromExternalLaunch();
        }
        else
        {
            _activationPending = true;
        }
    }

    private void App_Exit(object sender, ExitEventArgs e)
    {
        _singleInstanceCoordinator?.Dispose();
        _singleInstanceCoordinator = null;
    }

    private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (MainWindow is HandBrakeCompletedManager.App.MainWindow window)
        {
            window.PrepareForShutdown();
        }
    }
}

