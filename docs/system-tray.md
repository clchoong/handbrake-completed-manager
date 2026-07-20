# System Tray Behavior

HandBrake Completed Manager remains available in the Windows notification area while its main window is hidden.

## Window lifecycle

- Closing the main window hides it instead of terminating the application when Close to Tray is enabled.
- The first close-to-tray action displays guidance when notification-area guidance is enabled.
- Double-clicking the tray icon restores and activates the main window.
- Windows sign-out and shutdown bypass close-to-tray behavior so the operating system is not blocked.
- Only one desktop instance is allowed per signed-in Windows session. Launching the executable again signals the running instance to restore and activate its window, then the secondary process exits before creating a tray icon.

## Tray status and commands

The tray tooltip displays the current number of completed-history records. It updates when history is loaded or a record is removed.

The tray menu provides:

- **Open Completed Manager** to restore the main window.
- **Refresh history** to restore the window and reload SQLite history.
- **Exit** to remove the tray icon and terminate the application cleanly.

Tray Exit never waits synchronously for diagnostic logging. Shutdown removes the notification icon and releases the single-instance guard immediately, preventing a hidden half-closed process from intercepting the next launch.

Encode capture is performed by the separate receiver executable and does not require the main window to remain visible.

Versions before 0.3.1 did not enforce single-instance ownership. When upgrading from an older running copy, exit its existing tray icons once before starting the updated executable. Thereafter, copies launched from different folders share the same session-wide instance guard.
