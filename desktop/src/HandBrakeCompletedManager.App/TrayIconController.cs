using HandBrakeCompletedManager.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace HandBrakeCompletedManager.App;

internal sealed class TrayIconController : IDisposable
{
    private readonly Forms.ContextMenuStrip _contextMenu;
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _isDisposed;

    public TrayIconController(Action open, Action refresh, Action exit)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(exit);

        _contextMenu = new Forms.ContextMenuStrip();
        _contextMenu.Items.Add("Open Completed Manager", null, (_, _) => open());
        _contextMenu.Items.Add("Refresh history", null, (_, _) => refresh());
        _contextMenu.Items.Add(new Forms.ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, (_, _) => exit());

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Icon = Drawing.SystemIcons.Application,
            Text = TrayStatusFormatter.FormatRecordCount(0),
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => open();
    }

    public void UpdateRecordCount(int recordCount)
    {
        if (!_isDisposed)
        {
            _notifyIcon.Text = TrayStatusFormatter.FormatRecordCount(recordCount);
        }
    }

    public void ShowMovedToTrayNotification()
    {
        if (_isDisposed)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = "HandBrake Completed Manager is still running";
        _notifyIcon.BalloonTipText = "Double-click the tray icon to reopen the completed history.";
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }
}
