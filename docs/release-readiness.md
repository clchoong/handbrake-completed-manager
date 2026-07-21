# Release readiness

Version 0.8.0 was validated as a self-contained Windows x64 open-source release candidate on 21 July 2026.

## Validation result

- The complete Release solution build succeeded with zero warnings and zero errors.
- All 229 automated tests passed: 95 core, 70 infrastructure, and 64 finalisation tests.
- Direct replacement tests cover default output consumption, output retention, same-extension atomic replacement, copy cancellation, cleanup, retry, and persisted statuses.
- The version 0.8.0 database upgrade adds file-action outcomes without removing existing completed history or legacy recovery records.
- The Status column reports **Output Deleted**, **Source Replaced**, and **Source Replaced, Output Kept** rather than repeating HandBrake completion state.
- Single and bulk replacement expose both output-retention choices and refuse duplicate or unrelated occupied targets.
- Same-volume default replacement uses moves; cross-volume and keep-output transfers copy once with byte progress and cancellation.
- The source is not removed until a complete output is positioned in its source directory. Whole-file checksums and original-backup creation are not part of the direct workflow.
- All nine custom secondary windows use WPF software rendering and a forced first-frame layout refresh to address transparent popup content.
- The portable receiver recorded a completion event into a clean portable SQLite database.
- The receiver and desktop application used the same adjacent portable data and log locations.
- The packaged desktop application started and initialized successfully.
- Package verification removed all generated history, settings, logs, and temporary media before distribution.
- Archive inspection found no private developer metadata or local development paths.
- Packaged executable metadata reports product version `0.8.0` and company `clchoong`.

The validated archive is `HandBrake-Completed-Manager-0.8.0-win-x64.zip`. Its size is 116,231,438 bytes and its SHA-256 checksum is:

```text
6688308C0102AC9948D1BF8A8A9E2BA9B3001527F8E1F1730AAFB9158E24FEA6
```

## Supported release boundary

- Windows x64 is the validated architecture.
- The application supports Windows 10 version 1809/build 17763 or later and Windows 11.
- The package is self-contained and does not require administrator access or a separate .NET installation.
- HandBrake completion-action setup remains manual so the application does not alter HandBrake preferences.
- Direct source replacement is intentionally irreversible and displays that warning before either option can run.
- Separate **Recycle Output** still uses the Windows Recycle Bin and **Remove History** still changes only SQLite.
- Legacy Recovery remains available for unfinished verified operations created by earlier versions.
- The package is not code-signed, so Windows may display a reputation warning.

## Reproduce the checks

```powershell
dotnet build .\desktop\HandBrakeCompletedManager.sln --configuration Release
dotnet test .\desktop\HandBrakeCompletedManager.sln --configuration Release --no-build
.\scripts\publish-portable.ps1 -Version 0.8.0
```
