# HandBrake Completed Manager

[![CI](https://github.com/clchoong/handbrake-completed-manager/actions/workflows/ci.yml/badge.svg)](https://github.com/clchoong/handbrake-completed-manager/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

HandBrake's Queue is useful while an encode is in progress, but it is not a permanent source-to-output library. After completed entries are cleared or the window is closed, reconnecting a converted file with its original source can become a manual search across folders and drives.

HandBrake Completed Manager is a portable Windows companion that records each completed encode as it finishes. It keeps the source path, output path, file sizes, completion time, and replacement state available for later review, then provides safety-focused actions for locating files, rejecting an unsatisfactory output, or replacing a verified source.

It is intentionally a completed-encode history and file-management application—not a HandBrake queue viewer or queue-recovery tool.

> **Independent third-party project:** This project is not affiliated with, endorsed by, or maintained by the HandBrake project. “HandBrake” is used only to identify compatibility with the official [HandBrake](https://handbrake.fr/) application. No HandBrake source code, binaries, or graphic assets are included.

## Licence

The project's original source code is available under the [MIT License](LICENSE). Distributed builds also include the applicable .NET, Microsoft.Data.Sqlite, SQLitePCLRaw, and SQLite notices. HandBrake is separate software governed by [its own licence](https://github.com/HandBrake/HandBrake/blob/master/LICENSE).

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

Phase 1 established non-destructive completed-history management. The current replacement workflow provides a direct, cancellable cut-and-paste-style operation with an explicit irreversible-action warning. Older verified backup and recovery components remain available only for operations created by earlier versions.

## Implemented capabilities

The current Phase 1 implementation provides:

- The receiver accepts HandBrake completion values from command-line arguments or environment variables.
- Source and converted file metadata and size comparisons are captured.
- SQLite stores completed history and rejects repeated callbacks with the same fingerprint.
- Saved HandBrake activity logs can recover earlier successful encodes when the output still exists; paused, failed, malformed, missing-output, and duplicate logs are reviewed but skipped without modifying logs or media files.
- The WPF application displays history and aggregate size totals.
- History checks for receiver updates automatically while the application is open, but leaves the list and selection untouched when nothing changed.
- Installed, running, portable, saved, and manually selected HandBrake copies can be discovered.
- Test Pipeline validates the event-to-SQLite path in an isolated temporary database.
- An in-app guide provides the exact HandBrake completion-action executable and arguments.
- Selected history records support source/output playback and Explorer reveal; double-click opens the output, or the replacement file after the output has been consumed.
- Multi-term search, quick filters, correctly typed column sorting, live row numbering, result counts, and record details support history review.
- Ctrl-click, Shift-click, Ctrl+A, **Select all shown**, and **Clear selection** support deliberate multi-row management while single-file playback and reveal actions remain limited to one row.
- The history Status column records meaningful file outcomes: **Output Deleted**, **Source Replaced**, or **Source Replaced, Output Kept**.
- **Recycle output** verifies the selected output against its captured size and timestamp, blocks unfinished replacement dependencies, and moves the file to the Windows Recycle Bin while retaining the source and history record.
- Bulk output recycling lists every selected path, skips initially ineligible records, revalidates and recycles eligible outputs sequentially, supports stopping between items, and reports succeeded, failed, and skipped totals.
- Bulk source replacement lists every source and target, blocks duplicate targets, supports the same output-retention choice, and uses one persistent window with overall `processed/total` progress plus a byte-progress bar for every eligible item.
- Bulk Remove from History deletes only the selected SQLite records and never changes source or output files.
- Confirmed Remove from History deletes only the SQLite record and never modifies either video file.
- The notification-area icon supports close-to-tray, record-count status, Open, Refresh, and clean Exit commands.
- A session-wide single-instance guard prevents duplicate tray icons; launching another copy restores the already-running window and exits the new process.
- Local settings control startup visibility, close-to-tray, tray guidance, and history refresh interval.
- Non-fatal daily diagnostic logs cover desktop and receiver operational events.
- A marker-based portable mode keeps history, settings, connections, and logs beside the application.
- Release automation publishes and smoke-tests self-contained single-file desktop and receiver executables.
- **Replace Source** permanently moves the encoded output into the source library and is the default choice; **Replace Source and Keep Output** copies it instead.
- Same-volume default replacement uses fast filesystem moves. Cross-volume and keep-output operations show byte progress and can be cancelled before the destructive boundary.
- Direct replacement skips backup creation and SHA-256 verification. It checks the transferred file length and refuses unrelated occupied targets.
- Cancelled or failed transfers can be retried from the same history row.
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
- Legacy verified replacement and Recovery controls remain available for unfinished operations created by earlier releases.
- Bulk history removal closes its confirmation before opening a dedicated responsive progress window that identifies the active record and retains the final totals.
- Settings remain vertically scrollable at smaller window sizes and display scaling, and About reports the packaged version, MIT licence, independence notice, platform, runtime, storage boundary, and project links.
- A shared high-end desktop design system provides a refined media-library dashboard, semantic status treatments, responsive cards and tables, and consistently rendered secondary windows across Windows 10 and Windows 11.
- Completed-history cells use comfortable padding and centred text; the output-percentage cell becomes pale orange above 80% and pale red from 90%.
- After a separate path-specific confirmation, the verified original source can be moved to the Windows Recycle Bin. The operation re-hashes the source, backup, and promoted final file, persists intent before removal, fails instead of deleting permanently when recycling is unavailable, and recovers across either crash boundary.
- A final read-only integrity gate atomically marks the journal and replacement operation complete while updating source availability in history. The Recovery overview opens the matching history record directly and exposes only actions valid for its persisted checkpoint.
- Automated tests cover parsing, calculations, filtering, fingerprinting, persistence, duplicates, detection, connection state, file actions, guarded output recycling, and replacement-state presentation.

## Documentation

- [Product brief](docs/project-brief.md): product scope, requirements, safety rules, and release phases
- [Completion receiver](docs/completion-receiver.md): receiver interface, persistence, and local validation
- [HandBrake connection](docs/handbrake-connection.md): detection, pipeline testing, and completion-action configuration
- [HandBrake activity-log import](docs/handbrake-log-import.md): safe eligibility rules, deduplication, and recovery workflow
- [Completed history browsing](docs/history-file-actions.md): search, quick filters, sorting, details, file actions, and missing-file behavior
- [System tray behavior](docs/system-tray.md): close-to-tray lifecycle, status, commands, and Windows shutdown behavior
- [Settings and diagnostic logging](docs/settings-and-logging.md): local storage, available settings, log format, and privacy boundaries
- [About and software information](docs/about.md): version, licence, independence, platform, runtime, storage, and notice links
- [Portable release](docs/portable-release.md): package creation, Windows compatibility, storage modes, and verification
- [Release readiness](docs/release-readiness.md): validated release-candidate checks, supported boundary, and reproducible commands
- [Version 0.7.0 release notes](docs/releases/v0.7.0.md): simplified replacement, responsive bulk removal, Settings scrolling, and About
- [Version 0.7.1 release notes](docs/releases/v0.7.1.md): same-extension source replacement and reliable first-paint progress dialogs
- [Version 0.7.2 release notes](docs/releases/v0.7.2.md): corrected same-extension replacement messaging
- [Version 0.8.0 release notes](docs/releases/v0.8.0.md): direct cancellable replacement, output-retention choice, meaningful statuses, and popup rendering fixes
- [Version 0.8.1 release notes](docs/releases/v0.8.1.md): replacement playback, automatic bulk progression, stable refresh selection, table polish, and legacy-history removal
- [Version 0.9.0 release notes](docs/releases/v0.9.0.md): unified bulk progress, live numbering, padded cells, and output-percentage highlighting
- [Version 0.6.0 release notes](docs/releases/v0.6.0.md): saved-log recovery and desktop lifecycle fixes
- [Version 0.5.0 release notes](docs/releases/v0.5.0.md): multi-selection and bulk-management highlights, installation boundary, and checksum
- [Version 0.4.0 release notes](docs/releases/v0.4.0.md): first public-release highlights, installation boundary, and checksum
- [Direct source replacement](docs/one-click-safe-replacement.md): move/copy choices, cancellation, retry, and irreversible boundary
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

