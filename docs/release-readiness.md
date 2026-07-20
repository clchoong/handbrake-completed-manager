# Release readiness

Version 0.7.0 was validated as a self-contained Windows x64 open-source release candidate on 21 July 2026.

## Validation result

- The complete Release solution build succeeded with zero warnings and zero errors.
- All 221 automated tests passed: 94 core, 65 infrastructure, and 62 finalisation tests.
- The portable receiver recorded a completion event into a clean portable SQLite database.
- The receiver and desktop application used the same adjacent portable data and log locations.
- The packaged desktop application started and initialized successfully.
- Package verification removed all generated history, settings, logs, and temporary media before distribution.
- Archive inspection found only the desktop executable, receiver executable, portable marker, and portable guide.
- The WPF build embeds the high-contrast original application icon in the executable and uses it consistently in the dashboard and notification area.
- A real two-launch process check confirmed that the secondary process exits while the primary process remains and receives the activation signal.
- The history grid hands mouse-wheel input back to the page when its own scroll range reaches a boundary.
- Package verification requires the project MIT licence, direct dependency notices, and .NET distribution third-party notices.
- The archive contains no generated history, settings, logs, private developer metadata, or local paths outside the portable guide's generic examples.
- Output-recycling tests cover captured-file verification, unfinished-replacement refusal, Recycle Bin failure, source retention, and retained history.
- Replacement-state tests verify the terminal **Replaced** and **Restored** indicators from durable finalisation checkpoints.
- Extended row selection supports Ctrl-click, Shift-click, Ctrl+A, selecting all filtered rows, and clearing the selection.
- Bulk source replacement preflights every record and blocks selected records that resolve to the same final path before confirmation.
- Bulk source replacement, output recycling, and history removal execute sequentially, allow stopping between records, and report succeeded, failed, and skipped totals.
- Activity-log parsing tests cover successful, paused, failed, malformed, and fallback-timestamp cases.
- Activity-log integration tests verify missing-output refusal, completed-history deduplication, and non-destructive import.
- Real-log read-only validation parsed all 94 successful completion logs in the local sample set and correctly separated existing from missing outputs.
- Tray Exit no longer synchronously waits for logging on the UI thread, and history removal performs SQLite work away from the interface thread.
- The normal source-replacement path uses one exact-path warning followed by a dedicated overall progress window; detailed transaction controls remain available through Recovery.
- Bulk history removal yields for dialog rendering, then shows current-item and overall progress in a separate responsive window until final totals are available.
- Settings content scrolls independently of its footer, keeping diagnostic-log controls reachable at smaller sizes and display scaling.
- The About window reports the packaged assembly version, MIT licence, independence notice, platform, runtime, architecture, storage boundary, and project links.
- Packaged executable metadata reports product version `0.7.0`, company `clchoong`, and the expected copyright.

The validated archive is `HandBrake-Completed-Manager-0.7.0-win-x64.zip`. Its size is 116,214,145 bytes and its SHA-256 checksum is:

```text
22C94776C216F941B048949BEAA54A3A8C99825E8CA25B9F824DB76DAD231C4B
```

Generated packages remain outside source control. Rebuild and re-run the package verifier before publishing a later commit or version; a newly created archive can have a different checksum.

## Supported release boundary

- Windows x64 is the validated architecture.
- The application target permits Windows 10 version 1809/build 17763 or later. Supported deployment is limited to Windows editions still supported by Microsoft and Windows 11.
- The package is self-contained and does not require a separate .NET installation or administrator access.
- HandBrake completion-action setup remains manual so the application does not alter HandBrake preferences.
- Files retired by replacement or undo use forced Windows Recycle Bin semantics. There is no permanent-delete fallback.
- A separately confirmed output-recycling action verifies the captured file and refuses unfinished replacement dependencies before invoking the same recoverable Windows behavior.
- The package is not code-signed, so Windows may display a reputation warning.

## Reproduce the checks

From the repository root with the .NET 10 SDK installed:

```powershell
dotnet build .\desktop\HandBrakeCompletedManager.sln --configuration Release
dotnet test .\desktop\HandBrakeCompletedManager.sln --configuration Release --no-build
.\scripts\publish-portable.ps1 -Version 0.7.0
```

The publishing script performs package-level smoke tests and prints the generated archive checksum. See [Portable release](portable-release.md) for package layout and storage behavior.
