# Release readiness

Version 0.9.0 was validated as a self-contained Windows x64 open-source release candidate on 22 July 2026.

## Validation result

- The complete Release solution build succeeded with zero warnings and zero errors.
- All 240 automated tests passed: 106 core, 70 infrastructure, and 64 finalisation tests.
- Direct replacement tests cover default output consumption, output retention, same-extension atomic replacement, copy cancellation, cleanup, retry, and persisted statuses.
- Existing databases upgrade in place without removing completed history or legacy recovery records.
- The Status column reports **Output Deleted**, **Source Replaced**, and **Source Replaced, Output Kept** rather than repeating HandBrake completion state.
- Single and bulk replacement expose both output-retention choices and refuse duplicate or unrelated occupied targets.
- Same-volume default replacement uses moves; cross-volume and keep-output transfers copy once with byte progress and cancellation.
- The source is not removed until a complete output is positioned in its source directory. Whole-file checksums and original-backup creation are not part of the direct workflow.
- All ten custom secondary windows use WPF software rendering and a forced first-frame layout refresh to address transparent popup content.
- Double-click playback follows the effective replacement path after the default action consumes the output.
- Bulk replacement automatically advances after each item without a per-item completion click.
- Automatic receiver checks leave an unchanged history list and its selection intact.
- History removal clears legacy operation metadata before deleting its parent record, fixing foreign-key refusal without changing media files.
- Bulk source replacement uses one persistent window with an item-based total bar, a `processed/total` counter, and a byte-progress bar for every eligible file.
- The history table has comfortable cell padding, live 1-based visible numbering, and tested pale-orange/pale-red output-percentage thresholds.
- The portable receiver recorded a completion event into a clean portable SQLite database.
- The receiver and desktop application used the same adjacent portable data and log locations.
- The packaged desktop application started and initialized successfully.
- Package verification removed all generated history, settings, logs, and temporary media before distribution.
- Archive inspection found no private developer metadata or local development paths.
- Packaged executable metadata reports product version `0.9.0` and company `clchoong`.

The validated archive is `HandBrake-Completed-Manager-0.9.0-win-x64.zip`. Its size is 116,238,578 bytes and its SHA-256 checksum is:

```text
34C5AB5E96FE41047C03ED80B691096B2667BB88C5758800028C34179F26BDD4
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
.\scripts\publish-portable.ps1 -Version 0.9.0
```
