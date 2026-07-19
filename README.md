# HandBrake Completed Manager

HandBrake Completed Manager is a portable Windows companion application that maintains a durable history of completed HandBrake encodes and provides safety-focused file-management workflows.

## Platform

- Windows 10 version 1809 (build 17763) or later
- Windows 11
- .NET 10 LTS with WPF
- Self-contained portable distribution; no separate .NET installation or administrator access required

Microsoft support for Windows 10 is limited to Windows editions that remain in support. The application target still permits compatible Windows 10 build 1809+ systems to run it.

## Repository structure

```text
desktop/
  src/
    HandBrakeCompletedManager.App/             WPF desktop interface
    HandBrakeCompletedManager.Core/            Domain models and business rules
    HandBrakeCompletedManager.Infrastructure/  Database and operating-system integrations
    HandBrakeCompletedManager.Receiver/        HandBrake completion-event receiver
  tests/
    HandBrakeCompletedManager.Core.Tests/       Unit tests
    HandBrakeCompletedManager.Infrastructure.Tests/ Integration tests
database/
  migrations/                                   SQLite migrations
docs/
  project-brief.md                              Product requirements and phased scope
```

## Build and test

Install the .NET 10 SDK, then run:

```powershell
dotnet restore .\desktop\HandBrakeCompletedManager.sln
dotnet build .\desktop\HandBrakeCompletedManager.sln --configuration Release
dotnet test .\desktop\HandBrakeCompletedManager.sln --configuration Release
```

Create and verify the Windows x64 portable package:

```powershell
.\scripts\publish-portable.ps1
```

The generated folder and ZIP archive are placed under `artifacts\portable\win-x64\`, which is intentionally excluded from source control. See [Portable release](docs/portable-release.md) for package contents, storage behavior, and verification details.

The initial release is limited to non-destructive completed-history management. Automatic source replacement remains outside the initial scope.

Phase 2 development includes replacement safety preflight, persistent recovery state, and an explicitly confirmed user workflow that creates and verifies a separate temporary copy. No source backup, replacement, or deletion operation is enabled.

## Implemented capabilities

The current Phase 1 implementation provides:

- The receiver accepts HandBrake completion values from command-line arguments or environment variables.
- Source and converted file metadata and size comparisons are captured.
- SQLite stores completed history and rejects repeated callbacks with the same fingerprint.
- The WPF application displays history and aggregate size totals.
- History refreshes automatically while the application is open.
- Installed, running, portable, saved, and manually selected HandBrake copies can be discovered.
- Test Pipeline validates the event-to-SQLite path in an isolated temporary database.
- An in-app guide provides the exact HandBrake completion-action executable and arguments.
- Selected history records support source/output playback, Explorer reveal, and path copying; double-click opens the output.
- Multi-term search, quick filters, correctly typed column sorting, result counts, and record details support history review.
- Confirmed Remove from History deletes only the SQLite record and never modifies either video file.
- The notification-area icon supports close-to-tray, record-count status, Open, Refresh, and clean Exit commands.
- Local settings control startup visibility, close-to-tray, tray guidance, and history refresh interval.
- Non-fatal daily diagnostic logs cover desktop and receiver operational events.
- A marker-based portable mode keeps history, settings, connections, and logs beside the application.
- Release automation publishes and smoke-tests self-contained single-file desktop and receiver executables.
- A replacement preflight reports changed files, missing files, path conflicts, and unsafe metadata before a temporary copy can be prepared.
- Persistent replacement stages and progress fields support interruption recovery; source backup and final replacement remain disabled.
- The replacement review displays existing recovery state and can create a new `.hbcm-copying` file after explicit confirmation, with live progress, cancellation, durable state, and size/SHA-256 verification without modifying either original file.
- Automated tests cover parsing, calculations, filtering, fingerprinting, persistence, duplicates, detection, connection state, and file actions.

## Documentation

- [Product brief](docs/project-brief.md): product scope, requirements, safety rules, and release phases
- [Completion receiver](docs/completion-receiver.md): receiver interface, persistence, and local validation
- [HandBrake connection](docs/handbrake-connection.md): detection, pipeline testing, and completion-action configuration
- [Completed history browsing](docs/history-file-actions.md): search, quick filters, sorting, details, file actions, and missing-file behavior
- [System tray behavior](docs/system-tray.md): close-to-tray lifecycle, status, commands, and Windows shutdown behavior
- [Settings and diagnostic logging](docs/settings-and-logging.md): local storage, available settings, log format, and privacy boundaries
- [Portable release](docs/portable-release.md): package creation, Windows compatibility, storage modes, and verification
- [Replacement safety preflight](docs/replacement-preflight.md): review checks, planned paths, persistent recovery state, and disabled execution boundaries
- [Verified temporary copy](docs/temporary-copy-engine.md): streamed copy, progress, cancellation, verification, and retained recovery artifacts

