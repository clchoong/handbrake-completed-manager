# Release readiness

Version 0.3.0 was validated as a self-contained Windows x64 release candidate on 20 July 2026.

## Validation result

- The complete Release solution build succeeded with zero warnings and zero errors.
- All 210 automated tests passed: 87 core, 61 infrastructure, and 62 finalisation tests.
- The portable receiver recorded a completion event into a clean portable SQLite database.
- The receiver and desktop application used the same adjacent portable data and log locations.
- The packaged desktop application started and initialized successfully.
- Package verification removed all generated history, settings, logs, and temporary media before distribution.
- Archive inspection found only the desktop executable, receiver executable, portable marker, and portable guide.
- The WPF build embeds the original application icon in the executable and packages the matching dashboard brand asset.
- Output-recycling tests cover captured-file verification, unfinished-replacement refusal, Recycle Bin failure, source retention, and retained history.
- Replacement-state tests verify the terminal **Replaced** and **Restored** indicators from durable finalisation checkpoints.

The validated archive is `HandBrake-Completed-Manager-0.3.0-win-x64.zip`. Its size is 116,902,966 bytes and its SHA-256 checksum is:

```text
E55919E80C54897C7D67E3D65670B1257DCA2A00B98A46522994970B3DC4281C
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
.\scripts\publish-portable.ps1 -Version 0.3.0
```

The publishing script performs package-level smoke tests and prints the generated archive checksum. See [Portable release](portable-release.md) for package layout and storage behavior.
