# System Tray Behavior

HandBrake Completed Manager remains available in the Windows notification area while its main window is hidden.

## Window lifecycle

- Closing the main window hides it instead of terminating the application.
- The first close-to-tray action displays a notification explaining how to reopen the window.
- Double-clicking the tray icon restores and activates the main window.
- Windows sign-out and shutdown bypass close-to-tray behavior so the operating system is not blocked.

## Tray status and commands

The tray tooltip displays the current number of completed-history records. It updates when history is loaded or a record is removed.

The tray menu provides:

- **Open Completed Manager** to restore the main window.
- **Refresh history** to restore the window and reload SQLite history.
- **Exit** to remove the tray icon and terminate the application cleanly.

Encode capture is performed by the separate receiver executable and does not require the main window to remain visible.
