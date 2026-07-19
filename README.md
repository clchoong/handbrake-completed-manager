# HandBrake Completed Manager

HandBrake Completed Manager is a portable Windows companion application that maintains a durable history of completed HandBrake encodes and provides safety-focused file-management workflows.

## Platform

- Windows 10 version 1809 (build 17763) or later
- Windows 11
- .NET 10 LTS with WPF
- Self-contained portable distribution is planned for a future release

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

The initial release is limited to non-destructive completed-history management. Automatic source replacement remains outside the initial scope.

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
- Automated tests cover parsing, calculations, filtering, fingerprinting, persistence, duplicates, detection, connection state, and file actions.

## Documentation

- [Product brief](docs/project-brief.md): product scope, requirements, safety rules, and release phases
- [Completion receiver](docs/completion-receiver.md): receiver interface, persistence, and local validation
- [HandBrake connection](docs/handbrake-connection.md): detection, pipeline testing, and completion-action configuration
- [Completed history browsing](docs/history-file-actions.md): search, quick filters, sorting, details, file actions, and missing-file behavior
- [System tray behavior](docs/system-tray.md): close-to-tray lifecycle, status, commands, and Windows shutdown behavior

