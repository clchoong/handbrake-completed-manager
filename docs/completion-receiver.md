# Completion Event Receiver

`HandBrakeCompletedManager.Receiver` records a completed encode in the shared SQLite history database. It does not modify or delete source or converted files.

## Interface

The receiver accepts command-line options:

```text
--source <path>
--destination <path>
--destination-folder <path>
--exit-code <integer>
```

It also accepts the HandBrake environment variables:

```text
HB_SOURCE
HB_DESTINATION
HB_DESTINATION_FOLDER
HB_EXIT_CODE
```

Command-line values take precedence over environment variables. Source and destination paths are required. Exit code defaults to `0` when omitted, and destination folder is derived from the destination path when omitted.

## Persistence

By default, history is stored at:

```text
%LOCALAPPDATA%\HandBrake Completed Manager\history.db
```

Set `HBCM_DATA_DIRECTORY` to place `history.db` in a portable or test data directory.

## Idempotency

The receiver creates a fingerprint from normalized source and destination paths, file sizes, and destination modification time. SQLite enforces that fingerprint as unique. Repeated callbacks for the same completed output therefore return success without adding another row.

## Local validation example

```powershell
$env:HBCM_DATA_DIRECTORY = 'C:\Temp\HandBrakeCompletedManager'
dotnet .\desktop\src\HandBrakeCompletedManager.Receiver\bin\Release\net10.0-windows10.0.17763.0\HandBrakeCompletedManager.Receiver.dll `
  --source 'D:\Videos\Holiday.mov' `
  --destination 'E:\Converted\Holiday.mp4' `
  --exit-code 0
```

## HandBrake configuration

The desktop build places `HandBrakeCompletedManager.Receiver.exe` beside the main application. Expand **Configure completed-encode capture** in the application to copy its full path and the recommended HandBrake arguments. See [HandBrake detection and connection](handbrake-connection.md) for the complete setup procedure.
