using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace HandBrakeCompletedManager.App;

internal static class PopupWindowRendering
{
    public static void Stabilize(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource source &&
                source.CompositionTarget is not null)
            {
                source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            }
        };

        window.ContentRendered += (_, _) =>
            window.Dispatcher.BeginInvoke(() =>
            {
                window.InvalidateVisual();
                window.UpdateLayout();
            }, DispatcherPriority.Render);
    }
}
