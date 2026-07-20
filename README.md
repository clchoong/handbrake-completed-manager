# HandBrake Completed Manager

HandBrake's Queue is useful while an encode is in progress, but it is not a permanent source-to-output library. After completed entries are cleared or the window is closed, reconnecting a converted file with its original source can become a manual search across folders and drives.

HandBrake Completed Manager is a portable Windows companion that records each completed encode as it finishes. It keeps the source path, output path, file sizes, completion time, and replacement state available for later review, then provides safety-focused actions for locating files, rejecting an unsatisfactory output, or replacing a verified source.

It is intentionally a completed-encode history and file-management application—not a HandBrake queue viewer or queue-recovery tool.

> **Independent third-party project:** This project is not affiliated with, endorsed by, or maintained by the HandBrake project. “HandBrake” is used only to identify compatibility with the official [HandBrake](https://handbrake.fr/) application. No HandBrake source code, binaries, or graphic assets are included.

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
    HandBrakeCompletedManager.Finalization.Tests/ Transaction-journal and crash-recovery tests
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

Phase 1 established non-destructive completed-history management. Phase 2 adds replacement safety preflight, persistent recovery state, explicitly confirmed temporary and original-backup copies, guarded atomic promotion, forced Windows Recycle Bin source retirement, atomic forward completion, record-specific restart continuation, and a complete source-first undo workflow. Permanent deletion is not used for managed media-file retirement.

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
- Selected history records support source/output playback and Explorer reveal; double-click opens the output.
- Multi-term search, quick filters, correctly typed column sorting, result counts, and record details support history review.
- The history table clearly identifies source replacement as not replaced, in progress, needing attention, replaced, or restored.
- **Recycle output** verifies the selected output against its captured size and timestamp, blocks unfinished replacement dependencies, and moves the file to the Windows Recycle Bin while retaining the source and history record.
- Confirmed Remove from History deletes only the SQLite record and never modifies either video file.
- The notification-area icon supports close-to-tray, record-count status, Open, Refresh, and clean Exit commands.
- A session-wide single-instance guard prevents duplicate tray icons; launching another copy restores the already-running window and exits the new process.
- Local settings control startup visibility, close-to-tray, tray guidance, and history refresh interval.
- Non-fatal daily diagnostic logs cover desktop and receiver operational events.
- A marker-based portable mode keeps history, settings, connections, and logs beside the application.
- Release automation publishes and smoke-tests self-contained single-file desktop and receiver executables.
- A replacement preflight reports changed files, missing files, path conflicts, and unsafe metadata before a temporary copy can be prepared.
- Persistent replacement, original-backup, finalisation, and undo stages support record-specific interruption recovery through terminal `Completed` and `Undone` checkpoints.
- The replacement review displays existing recovery state and can create a new `.hbcm-copying` file after explicit confirmation, with live progress, cancellation, durable state, and size/SHA-256 verification without modifying either original file.
- Retained temporary artifacts can be permanently discarded after a separate confirmation; cleanup validates every recorded path, refuses active file locks or stale state, and immediately re-runs preflight for a safe retry.
- After the converted temporary copy is verified, the original source can be streamed to a create-new backup path with independent progress, cancellation, durable state, and size/SHA-256 verification while the source remains untouched.
- Verified or partial backup artifacts can be explicitly discarded through the same exact-path, stale-state, and active-lock safety boundaries.
- A read-only finalisation readiness review revalidates persisted state, exact paths, sizes, SHA-256 equality, file stability, and final-path availability without changing any file.
- A restart-recovery overview inventories incomplete replacement operations and suggests the next safe review action; it does not perform recovery automatically.
- Successful readiness review persists a revisioned transaction journal containing verified source and final-file digests.
- After explicit confirmation, the prepared temporary copy can be atomically renamed to an unoccupied final path while read locks protect the source and backup. Intent-first recovery handles interruption before or after the rename without overwriting a file.
- The desktop undo workflow explicitly prepares undo, reconstructs the source through a resumable verified artifact, recycles the promoted final only after source verification, and atomically restores source availability in history.
- After reviewing all planned paths, **Replace source safely** runs temporary-copy verification, original-backup verification, atomic promotion, Windows Recycle Bin source retirement, and atomic completion from one path-specific confirmation, with live progress and durable recovery checkpoints.
- A shared high-end desktop design system provides a refined media-library dashboard, semantic status treatments, responsive cards and tables, a focused one-click replacement experience, and collapsed advanced recovery controls across Windows 10 and Windows 11.
- After a separate path-specific confirmation, the verified original source can be moved to the Windows Recycle Bin. The operation re-hashes the source, backup, and promoted final file, persists intent before removal, fails instead of deleting permanently when recycling is unavailable, and recovers across either crash boundary.
- A final read-only integrity gate atomically marks the journal and replacement operation complete while updating source availability in history. The Recovery overview opens the matching history record directly and exposes only actions valid for its persisted checkpoint.
- Automated tests cover parsing, calculations, filtering, fingerprinting, persistence, duplicates, detection, connection state, file actions, guarded output recycling, and replacement-state presentation.

## Documentation

- [Product brief](docs/project-brief.md): product scope, requirements, safety rules, and release phases
- [Completion receiver](docs/completion-receiver.md): receiver interface, persistence, and local validation
- [HandBrake connection](docs/handbrake-connection.md): detection, pipeline testing, and completion-action configuration
- [Completed history browsing](docs/history-file-actions.md): search, quick filters, sorting, details, file actions, and missing-file behavior
- [System tray behavior](docs/system-tray.md): close-to-tray lifecycle, status, commands, and Windows shutdown behavior
- [Settings and diagnostic logging](docs/settings-and-logging.md): local storage, available settings, log format, and privacy boundaries
- [Portable release](docs/portable-release.md): package creation, Windows compatibility, storage modes, and verification
- [Release readiness](docs/release-readiness.md): validated release-candidate checks, supported boundary, and reproducible commands
- [One-click safe source replacement](docs/one-click-safe-replacement.md): single-confirmation execution, stage ordering, refusal rules, and recovery behavior
- [Desktop UI design](docs/desktop-ui-design.md): visual system, information hierarchy, replacement experience, accessibility, and scaling behavior
- [Publishing and independence](docs/publishing-and-independence.md): HandBrake relationship, GPL boundary, branding, and repository licensing decisions
- [Replacement safety preflight](docs/replacement-preflight.md): review checks, planned paths, persistent recovery state, and disabled execution boundaries
- [Verified temporary copy](docs/temporary-copy-engine.md): streamed copy, progress, cancellation, verification, and retained recovery artifacts
- [Original-backup preparation](docs/original-backup.md): non-destructive source backup, verification, cancellation, cleanup, and current safety boundary
- [Finalisation readiness and restart recovery](docs/finalization-readiness.md): read-only integrity gate, recovery classifications, and disabled mutation boundary
- [Finalisation transaction design](docs/finalization-transaction-design.md): durable checkpoints, forward/undo ordering, crash decisions, and execution boundary
- [Atomic final-file promotion](docs/atomic-final-promotion.md): confirmation, protected rename, refusal rules, and restart recovery
- [Verified source restoration](docs/source-restoration.md): resumable verified copy, atomic non-overwriting restore, refusal rules, and crash recovery
- [Guarded source recycling](docs/source-recycling.md): confirmation, forced Windows Recycle Bin behavior, protected verification, and intent-first recovery
- [Forward completion and restart continuation](docs/forward-completion-and-recovery.md): final integrity gate, atomic SQLite completion, idempotency, and direct record recovery
- [Guarded replacement undo](docs/undo-workflow.md): source-first ordering, resumable restoration, promoted-final recycling, atomic completion, and restart recovery

