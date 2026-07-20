# Release readiness

Version 0.2.0 was validated as a self-contained Windows x64 release candidate on 20 July 2026.

## Validation result

- The complete Release solution build succeeded with zero warnings and zero errors.
- All 205 automated tests passed: 87 core, 56 infrastructure, and 62 finalisation tests.
- The portable receiver recorded a completion event into a clean portable SQLite database.
- The receiver and desktop application used the same adjacent portable data and log locations.
- The packaged desktop application started and initialized successfully.
- Package verification removed all generated history, settings, logs, and temporary media before distribution.
- Archive inspection found only the desktop executable, receiver executable, portable marker, and portable guide.
- The redesigned WPF application passed live Windows UI Automation checks for control loading, on-screen bounds, minimum-width wrapping, and accessible names on its primary search, filter, connection, and safe-replacement inputs.

The validated archive is `HandBrake-Completed-Manager-0.2.0-win-x64.zip`. Its size is 115,741,219 bytes and its SHA-256 checksum is:

```text
DF1AE61169EF1F8E63B56665BE392251432A66A13B0F790BD56C02B0E86A5412
```

Generated packages remain outside source control. Rebuild and re-run the package verifier before publishing a later commit or version; a newly created archive can have a different checksum.

## Supported release boundary

- Windows x64 is the validated architecture.
- The application target permits Windows 10 version 1809/build 17763 or later. Supported deployment is limited to Windows editions still supported by Microsoft and Windows 11.
- The package is self-contained and does not require a separate .NET installation or administrator access.
- HandBrake completion-action setup remains manual so the application does not alter HandBrake preferences.
- Files retired by replacement or undo use forced Windows Recycle Bin semantics. There is no permanent-delete fallback.
- The package is not code-signed, so Windows may display a reputation warning.

## Reproduce the checks

From the repository root with the .NET 10 SDK installed:

```powershell
dotnet build .\desktop\HandBrakeCompletedManager.sln --configuration Release
dotnet test .\desktop\HandBrakeCompletedManager.sln --configuration Release --no-build
.\scripts\publish-portable.ps1 -Version 0.2.0
```

The publishing script performs package-level smoke tests and prints the generated archive checksum. See [Portable release](portable-release.md) for package layout and storage behavior.
