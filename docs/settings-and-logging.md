# Settings and Diagnostic Logging

Application settings and diagnostic logs are machine-local runtime data. They are excluded from source control and are never written into a HandBrake installation directory.

## Storage

Installed-mode storage uses:

```text
%LOCALAPPDATA%\HandBrake Completed Manager\settings.json
%LOCALAPPDATA%\HandBrake Completed Manager\logs\
```

When `HBCM_DATA_DIRECTORY` is set, both locations are resolved beneath that directory for custom deployments and testing. In the packaged portable edition, the adjacent `portable.mode` marker automatically places settings and logs under `data` beside the executables. See [Portable release](portable-release.md).

Settings are written atomically through a temporary file. Invalid JSON or unsupported refresh intervals fall back to safe defaults.

## Available settings

The Settings window provides:

- Start with the main window hidden in the notification area.
- Close the main window to the notification area.
- Enable or disable notification-area guidance.
- Refresh completed history every 3, 5, 10, 30, or 60 seconds.
- Open the diagnostic log folder.

The settings content scrolls independently of the fixed Save/Cancel footer, so the diagnostic-log section remains reachable on smaller windows and higher Windows display scaling.

Changes take effect immediately except Start Minimized, which applies on the next application launch.

## Diagnostic logs

The desktop application and completion receiver write daily UTF-8 text logs named:

```text
handbrake-completed-manager-YYYYMMDD.log
```

Each entry contains an ISO 8601 timestamp, severity, component, and operational message. Multiline text is normalized to a single line. Logging failures are non-fatal and cannot prevent history capture or application startup.

Logs record startup, settings changes, history-loading failures, HandBrake detection and pipeline results, history removal, tray lifecycle, and receiver outcomes. File contents are never logged. Paths are included only when an exception requires them for diagnosis.
