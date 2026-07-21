# Portable release

The portable edition runs on 64-bit Windows without administrator access or a separately installed .NET runtime. The default package targets `win-x64` and has a minimum application target of Windows 10 version 1809 (build 17763).

Microsoft's current [.NET 10 Windows support matrix](https://learn.microsoft.com/dotnet/core/install/windows#supported-versions) limits supported Windows 10 use to editions that remain in support, including listed Enterprise, IoT, and LTSC releases. Windows 11 remains supported according to the same matrix. An out-of-support Windows edition may still start the application, but it is not a supported deployment.

## Build the package

From the repository root with the .NET 10 SDK installed:

```powershell
.\scripts\publish-portable.ps1
```

The script restores and publishes both executables as self-contained single files, performs package-level smoke tests, creates the package, and prints its SHA-256 checksum:

```text
artifacts\portable\win-x64\
├── HandBrake Completed Manager\
│   ├── HandBrakeCompletedManager.exe
│   ├── HandBrakeCompletedManager.Receiver.exe
│   ├── LICENSE.txt
│   ├── THIRD-PARTY-NOTICES.txt
│   ├── DOTNET-THIRD-PARTY-NOTICES.txt
│   ├── portable.mode
│   └── PORTABLE-README.txt
└── HandBrake-Completed-Manager-0.7.2-win-x64.zip
```

Build output under `artifacts` is intentionally excluded from source control. A different semantic version can be selected explicitly:

```powershell
.\scripts\publish-portable.ps1 -Version 0.7.2
```

The first verified release target is Windows x64. Other architectures should be added only with architecture-appropriate package execution tests.

## Portable storage

The `portable.mode` marker makes both executables use the same `data` directory beside the application:

```text
HandBrake Completed Manager\
├── HandBrakeCompletedManager.exe
├── HandBrakeCompletedManager.Receiver.exe
├── LICENSE.txt
├── THIRD-PARTY-NOTICES.txt
├── DOTNET-THIRD-PARTY-NOTICES.txt
├── portable.mode
└── data\
    ├── history.db
    ├── settings.json
    ├── handbrake-connections.json
    └── logs\
```

The data directory is created on first use. Keep `portable.mode` and both executables together. Exit the notification-area application before copying, moving, or backing up the folder.

Storage selection follows this order:

1. `HBCM_DATA_DIRECTORY`, when explicitly set for testing or a custom deployment.
2. The adjacent `data` directory when `portable.mode` exists.
3. `%LOCALAPPDATA%\HandBrake Completed Manager` for a normal non-portable build.

The application never writes configuration into a HandBrake program directory. If the portable folder is moved, update the receiver executable path in HandBrake preferences.

## Automated verification

`verify-portable-package.ps1` checks that:

- Both required executables and the portable marker exist.
- The executables are self-contained single files rather than framework-dependent output.
- The receiver records a sample completion into `data\history.db`.
- Receiver and desktop diagnostics share the portable `data\logs` directory.
- The desktop executable starts and remains running long enough to initialize its history.
- Smoke-test history, settings, logs, and temporary media files are removed from the final package.
- Verification refuses to run when a package already contains a `data` directory, preventing existing user data from being modified.

The package is not currently code-signed. Windows may therefore display a reputation warning until a trusted signing certificate and established publisher reputation are available.

## HandBrake setup

Run `HandBrakeCompletedManager.exe`, expand **Configure completed-encode capture**, and copy the displayed receiver executable path and arguments into HandBrake. The full procedure is documented in [HandBrake detection and connection](handbrake-connection.md).
