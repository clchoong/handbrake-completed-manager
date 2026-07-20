# Product Brief: HandBrake Completed Manager

## Product overview

HandBrake Completed Manager is a Windows companion application that maintains a durable record of completed HandBrake encoding tasks. When HandBrake's completed entries are cleared or its window is closed, users may otherwise need to search folders and drives to reconnect an output with its original source. The companion preserves that source-to-output relationship as each encode finishes.

The product is a dedicated completed-encode history and file-management application. Mirroring pending or running HandBrake tasks, queue recovery, and restoration of HandBrake's internal queue are outside its scope. HandBrake remains the authoritative interface for active encoding work.

## Problem statement

When completed encodes are removed from HandBrake's Queue, users lose convenient access to:

* The original source file
* The converted file
* Original file size
* Converted file size
* Converted size as a percentage of the source
* Space saved
* Source folder
* Converted file folder
* HandBrake completion history

The product must also restore convenient file actions comparable to those available from HandBrake's Queue.

For converted files that fail manual quality review, the completed-history interface provides a separately confirmed **Recycle output** action. It retains the source and history record, refuses files that changed since capture, refuses unfinished replacement dependencies, and uses the Windows Recycle Bin without a permanent-delete fallback.

## Target platform

Build the first version for Windows only.

The app should support:

* Installed HandBrake
* Portable HandBrake
* HandBrake ZIP versions
* Multiple HandBrake installations
* HandBrake stored on another drive
* HandBrake stored on a removable drive

The companion application itself should preferably be portable and not require administrator access.

## Technology

The application uses C#, .NET 10 LTS, and WPF. This stack provides mature Windows file-system and process integration, system-tray support, SQLite compatibility, and straightforward portable distribution.

## HandBrake integration

HandBrake for Windows can launch an external application after each successful encode.

HandBrake provides information such as:

* Source file path
* Destination file path
* Destination folder
* Exit code

These may be received through command-line arguments or environment variables:

* `HB_SOURCE`
* `HB_DESTINATION`
* `HB_DESTINATION_FOLDER`
* `HB_EXIT_CODE`

The Completed Manager should support being launched by HandBrake after each successful encode.

Example behaviour:

1. HandBrake finishes an encode.
2. HandBrake launches Completed Manager or a lightweight receiver executable.
3. The receiver reads the source and destination paths.
4. It records the completed encode.
5. It calculates the file-size comparison.
6. It updates the desktop application.
7. It exits quietly or keeps the system-tray application running.

Do not depend on reading HandBrake’s internal Queue file because completed tasks may be excluded from it.

As a separate recovery path, the application may read HandBrake's saved activity logs. Import is allowed only for logs that contain an unambiguous successful-completion result, valid absolute source and destination paths, and a destination file that still exists. The workflow must preview every candidate, deduplicate completed history, retain useful missing-source paths, and never modify HandBrake logs, settings, Queue data, or media files.

## HandBrake detection

Include a setup wizard with a **Find HandBrake** function.

Detection order:

1. Detect currently running `HandBrake.exe` processes.
2. Read the full executable path from each running process.
3. Search common installation folders:

   * `C:\Program Files\HandBrake\`
   * `C:\Program Files (x86)\HandBrake\`
4. Check Windows installation records where appropriate.
5. Check Start menu shortcuts.
6. Allow the user to select folders to search.
7. Provide a manual Browse button.

The app should support multiple detected HandBrake copies.

Example:

| Version            | Type      | Location                   | Status        |
| ------------------ | --------- | -------------------------- | ------------- |
| HandBrake Stable   | Installed | C:\Program Files\HandBrake | Connected     |
| HandBrake Portable | Portable  | D:\Portable Apps\HandBrake | Connected     |
| HandBrake Nightly  | Portable  | E:\Video Tools\HandBrake   | Not connected |

The completed history should be shared across all HandBrake installations.

If a portable HandBrake folder is moved, show:

* Previous location
* Find Again
* Browse
* Remove Location

The history database must not depend on the HandBrake installation location.

## Connection setup

Create a simple connection wizard:

### Connect HandBrake

* Detected HandBrake path
* Detected version
* Installed or portable type
* Connect Automatically
* Manual Setup
* Test Connection

A Test Connection button should create a harmless test record or send a simulated completed-encode event.

The app should clearly show:

* Connected
* Not connected
* HandBrake detected but not configured
* HandBrake location missing

Where possible, automatically configure the HandBrake completed-action settings.

If automatic configuration is unsafe or unsupported, show precise manual instructions for:

* Executable path
* Arguments
* Environment variables
* HandBrake Preferences location

Do not modify HandBrake program files.

## Completed history database

Use SQLite for persistent storage.

Suggested storage location:

`%LOCALAPPDATA%\HandBrake Completed Manager\`

Store:

* Record ID
* HandBrake instance
* HandBrake version
* Completion date and time
* Source path
* Source filename
* Source extension
* Source size
* Destination path
* Destination filename
* Destination extension
* Destination size
* Output percentage
* Space saved percentage
* Space saved in bytes
* Encode exit code
* Current status
* Replacement status
* Replacement progress
* Verification status
* Original backup path
* Backup expiry date
* Notes
* Date created
* Date updated

Use a unique identity based on appropriate fields such as:

* Source path
* Destination path
* Completion timestamp
* File size

Prevent accidental duplicate records when HandBrake calls the receiver more than once.

## Main completed list

The main screen should show all completed tasks.

Suggested columns:

| Completed           | Status    | Source      | Original Size | Converted Size | Output Size | Space Saved |
| ------------------- | --------- | ----------- | ------------: | -------------: | ----------: | ----------: |
| 19 Jul 2026, 5.30pm | Completed | Holiday.mov |      10.00 GB |        4.00 GB |         40% |         60% |

Calculations:

`Output percentage = Converted size ÷ Original size × 100`

`Space saved percentage = 100 − Output percentage`

Also display the actual storage saved.

Example:

* Source: 10.00 GB
* Converted: 4.00 GB
* Output size: 40% of source
* Space saved: 6.00 GB
* Space saved percentage: 60%

Handle missing source or destination files gracefully.

## Dashboard summary

Show summary information at the top:

* Total completed encodes
* Total original size
* Total converted size
* Total storage saved
* Average size reduction
* Number awaiting review
* Number with source replaced
* Number with replacement failures

Example:

* Completed encodes: 326
* Original size: 2.84 TB
* Converted size: 1.17 TB
* Space saved: 1.67 TB
* Average reduction: 58.8%

## Row actions

Each completed record should support:

* Play source file
* Play converted file
* Open source folder
* Open converted folder
* Select source file in File Explorer
* Select converted file in File Explorer
* Replace source
* Retry replacement
* Undo replacement
* Recycle converted output
* Remove from history
* Add note
* View technical details

Double-clicking a row should play the converted file by default.

Clearly separate these actions:

* Remove from history
* Delete source file
* Delete converted file

Removing a record must never delete either video file.

## Replace Source feature

Add a **Replace Source** button.

Purpose:

Create a verified converted file beside the original source, retain a verified backup, and only then recycle or archive the original source through a recoverable transaction.

Example:

Before:

* `D:\Videos\Holiday.mov`
* `E:\Converted\Holiday.mp4`

After:

* `D:\Videos\Holiday.mp4`

The app should keep the original base filename while retaining the converted extension.

Example:

`My Holiday Recording.mov`

becomes:

`My Holiday Recording.mp4`

## Safe replacement workflow

The replacement operation is destructive, so use a safe process.

The normal interface presents one clear exact-path warning followed by one overall file-copy-style progress window. Internal preflight, copy, backup, verification, promotion, recycling, and transaction checkpoints remain automatic. Detailed transaction controls belong in Recovery rather than the normal replacement path.

1. Confirm the source file exists.
2. Confirm the converted file exists.
3. Confirm the converted file is not still being written.
4. Display both file sizes.
5. Display expected storage savings.
6. Check for filename conflicts.
7. Validate the converted file.
8. Copy the converted file to a temporary filename in the source location.
9. Show live transfer progress.
10. Verify the completed copy.
11. Copy and verify the original source in its backup folder.
12. Persist promotion intent, then atomically rename the copied converted file to its final name.
13. Verify the promoted final file while the original source remains present.
14. After the user confirms the reviewed replacement, persist removal intent and recycle or archive the original source.
15. Mark the record as Source Replaced.
16. Retain a revisioned transaction journal and enough verified information to undo the replacement.

Never delete the source before the converted file has been copied and verified.

For transfers between different drives, always:

1. Copy
2. Verify
3. Archive or delete original

Do not depend on a normal file move across drives.

## Live transfer tracking

Show live progress:

* Bytes transferred
* Total bytes
* Percentage
* Transfer speed
* Estimated time remaining
* Current file
* Current stage

Example:

### Replacing source

* Transferred: 2.8 GB / 5.4 GB
* Progress: 52%
* Speed: 145 MB/s
* Remaining: 18 seconds
* Stage: Copying converted file

Stages may include:

* Preparing
* Checking destination
* Copying
* Verifying
* Backing up source
* Finalising
* Completed
* Failed
* Cancelled

## Transfer interruption and retry

The replacement may fail because of:

* Disconnected external drive
* USB interruption
* Network interruption
* Insufficient disk space
* Windows restart
* Application crash
* File lock
* Permission issue

Persist transfer state in SQLite.

After reopening the app, show incomplete operations.

Actions:

* Retry
* Resume if technically safe
* Restart Transfer
* Cancel
* Open temporary file location
* Restore original
* Clean temporary files

Do not automatically assume that a partially copied file is valid.

If reliable resuming is difficult, restart the copy safely rather than pretending to resume.

## Verification

Before deleting or archiving the source, verify the converted file.

Basic checks:

* File exists
* Size is greater than zero
* File is no longer growing
* File can be opened
* Video stream exists
* Audio stream exists when expected
* Duration is close to the source duration
* Destination size matches the completed copy

Use `ffprobe` for technical verification if appropriate.

Display the verification result:

* Verified
* Verification warning
* Verification failed
* Not verified

Possible warnings:

* Duration mismatch
* No audio stream
* No video stream
* Output is unexpectedly small
* Output is larger than source
* Source metadata unavailable

Do not delete the source when verification fails.

Allow the user to review and override a warning, but require clear confirmation.

## Original backup and Undo

Default behaviour should be to move the original source into a backup folder instead of deleting it immediately.

Suggested location:

`Source Folder\HandBrake Original Backup\`

Retention choices:

* 1 day
* 3 days
* 7 days
* 30 days
* Keep permanently
* Delete immediately

Default to 7 days.

During the retention period, allow:

* Undo replacement
* Restore original
* Delete backup now
* Extend retention

The app should automatically clean expired backups only when it is safe to do so.

Show:

* Backup location
* Backup expiry date
* Backup size
* Undo availability

## Replacement statuses

Suggested statuses:

* Completed
* Awaiting Review
* Replacing
* Verifying
* Original Backed Up
* Source Replaced
* Replacement Failed
* Replacement Cancelled
* Source Missing
* Converted File Missing
* Output Moved
* Verification Warning
* Verified

For replaced records, display:

* Source Replaced
* Replacement date and time
* Original backup location
* Backup expiry date
* Final replacement path

## Bulk actions

Allow extended row selection through Ctrl-click, Shift-click, Ctrl+A, **Select all shown**, and **Clear selection**. Ordinary playback, reveal, details, and the advanced replacement/recovery window remain available only when exactly one record is selected.

Bulk actions:

* Replace selected sources
* Verify selected outputs
* Remove selected records from history
* Recycle selected converted files through the Windows Recycle Bin
* Move selected outputs
* Export selected records
* Add tags

Bulk replacements should run one by one through a managed queue.

If one replacement fails:

* Mark it failed
* Continue with the remaining items
* Show a final summary

Do not run many destructive file replacements simultaneously by default.

Bulk output recycling must verify every selected record before changing files, present the exact affected paths and eligibility failures, require one explicit confirmation, and report a per-item result. It must never fall back to permanent deletion.

Bulk source replacement is a separate managed workflow rather than a loop around the single-record dialog. It requires sequential execution, durable per-record progress, cancellation between records, restart recovery, and a final succeeded/skipped/failed summary.

Implemented bulk file actions display every selected source and target, skip initial preflight failures, block duplicate final paths across the selection, execute records sequentially, expose **Stop after current**, continue after a record-specific safe failure, and present a final result summary.

## Duplicate and conflict detection

Warn when:

* Destination filename already exists
* Another history record has the same source
* The same source was converted more than once
* Destination path matches the source path
* Output extension matches the source extension; this uses a separately verified atomic in-place replacement path
* The converted file has already replaced the source
* A temporary file from a previous operation exists

Provide conflict choices:

* Skip
* Rename
* Replace existing converted file
* Cancel
* Review manually

Do not overwrite an unrelated file automatically.

## Search and filters

Support:

* Keyword search
* Source filename
* Destination filename
* Source folder
* Destination folder
* Date range
* HandBrake installation
* Status
* File extension
* Size range
* Replacement state

Quick filters:

* Today
* This week
* This month
* Awaiting review
* Not replaced
* Source replaced
* Replacement failed
* Source missing
* Output missing
* Largest files
* Largest storage savings
* Output larger than source

## Sorting

Allow sorting by:

* Completion date
* Source size
* Converted size
* Output percentage
* Storage saved
* Filename
* Status
* Replacement date

## Delete completed list

Allow the user to clear the history.

Options:

* Remove selected records
* Remove all records
* Remove records older than a selected date
* Remove records whose source and output are both missing
* Remove successfully replaced records

Removing history records must not delete files unless the user separately chooses a file-deletion action.

Before clearing history, offer an optional database backup or CSV export.

## File deletion

When deleting actual files:

* Prefer moving files to the Windows Recycle Bin
* Clearly show which file will be deleted
* Show the full path
* Show file size
* Require confirmation for permanent deletion
* Never use permanent deletion as the default

## System tray

Provide a system-tray mode.

Tray information:

* Connected HandBrake instances
* Tracking status
* Number of completed records
* Last completed encode
* Active replacement transfer
* Failed operation warning

Tray actions:

* Open Completed Manager
* Pause tracking
* Resume tracking
* Test connection
* Find HandBrake
* Exit

Example:

### HandBrake Completed Manager

* Connected to HandBrake
* Watching completed encodes
* 18 completed records
* Last encode completed 3 minutes ago

If HandBrake is running but tracking is not configured:

* HandBrake detected but completion tracking is not configured
* Fix Connection

## Notifications

Optional Windows notifications:

* Encode recorded
* Encode completed with size reduction
* Output is larger than source
* Source or output missing
* Replacement completed
* Replacement failed
* Original backup will expire soon
* Connection to HandBrake needs attention

Allow notifications to be disabled.

## Settings

Include:

### General

* Start with Windows
* Start minimised
* Close to system tray
* Database location
* Date and size display
* Theme
* Notification settings

### HandBrake Connections

* Detected installations
* Connected installations
* Find HandBrake
* Browse
* Test Connection
* Remove Connection
* Reconfigure Connection

### Replacement

* Default backup retention
* Copy verification level
* Use Recycle Bin
* Automatically verify completed encodes
* Automatically mark as awaiting review
* Filename rules
* Conflict behaviour
* Temporary file suffix

### External Tools

* FFmpeg path
* ffprobe path
* Auto-detect external tools
* Download instructions if not present

Do not automatically download software without the user’s confirmation.

## Portable application behaviour

The app should be able to run as a portable Windows application.

Possible portable storage structure:

```text
HandBrake Completed Manager\
├── HandBrakeCompletedManager.exe
├── data\
│   └── history.db
├── logs\
├── backups\
└── settings.json
```

Offer two modes:

* Portable mode
* Installed mode using Local AppData

Do not store configuration inside the HandBrake directory.

## Logging

Create clear application logs for:

* HandBrake events received
* Database operations
* File transfers
* Verification
* Replacement
* Backup cleanup
* Errors

Do not log private file contents.

Log file paths only as necessary for diagnostics.

Provide:

* Open Log Folder
* Export Diagnostic Report
* Clear Old Logs

## Error handling

The app must not crash because:

* A file was moved
* A drive was disconnected
* A path contains Unicode characters
* A path is very long
* A file is locked
* A folder is read-only
* The database is temporarily locked
* FFmpeg is not installed
* HandBrake is not running

Show practical error messages with recovery actions.

## User interface

Use a refined native Windows desktop interface with a consistent visual system, clear hierarchy, semantic status colors, responsive layouts, and progressive disclosure for advanced recovery controls.

Suggested layout:

### Left sidebar

* All Completed
* Awaiting Review
* Source Replaced
* Replacement Failed
* Missing Files
* HandBrake Connections
* Settings

### Main area

* Summary cards
* Search
* Filters
* Completed history table
* Details panel

### Details panel

* Source information
* Converted information
* Size comparison
* Verification
* Replacement history
* Backup information
* Available actions

Use clear status labels and avoid overly technical wording in the main interface.

## Safety rules

These rules are mandatory:

1. Never delete the source before the converted file is copied and verified.
2. Never overwrite an unrelated file automatically.
3. Never combine Remove from History with Delete File.
4. Never permanently delete by default.
5. Keep enough state to recover from an interrupted replacement.
6. Use temporary filenames during transfer.
7. Keep the original source backup by default.
8. Confirm destructive bulk actions.
9. Do not automatically replace sources in the initial version.
10. Do not depend on HandBrake’s completed Queue history.

Output recycling remains separate from Remove from History and source replacement. A successful recycle updates only the recorded availability of the converted output.

## Initial release scope

Build the project in phases.

### Phase 1: Completed history MVP

* Windows desktop app
* SQLite database
* Receive HandBrake completion events
* Record source and destination
* Calculate size comparison
* Completed history table
* Play and open file actions
* Open and select files in File Explorer
* Guarded output recycling through the Windows Recycle Bin
* Remove records from history
* Detect installed and portable HandBrake
* Manual connection instructions
* System tray
* Basic settings
* Logs

### Phase 2: Safe source replacement

* Replace Source
* One-confirmation execution after path and safety review
* Temporary copy
* Live progress
* Verification
* Backup original
* Undo replacement
* Retry failed operations
* Persistent transfer state
* Replacement status

### Phase 3: Advanced management

* Bulk replacement
* ffprobe verification
* Automatic backup expiry
* Duplicate detection
* Advanced filtering
* CSV export
* Diagnostic report
* Multiple HandBrake connection management

Do not start with automatic source replacement.

## Acceptance criteria for the MVP

The MVP is complete when:

1. HandBrake completes an encode.
2. The companion app records the source and destination automatically.
3. The record remains after HandBrake and Windows are restarted.
4. The app displays original size and converted size.
5. The app displays output percentage and space saved.
6. The user can play the source and output.
7. The user can open and select both files in File Explorer.
8. Removing the history record does not delete either file.
9. Installed and portable HandBrake copies can be detected or selected manually.
10. Duplicate callbacks do not create duplicate records.
11. Missing files are clearly marked.
12. The application can run from a portable folder.

## Implementation sequence

Development proceeds in the following order:

1. Establish the project architecture.
2. Define the SQLite schema.
3. Implement the completion-event receiver.
4. Implement the MVP desktop interface.
5. Add HandBrake detection and setup.
6. Add automated tests for calculations and duplicate detection.
7. Add integration tests for receiving environment variables.
8. Prepare a portable Windows build.
9. Document the HandBrake connection procedure.

The codebase must remain structured and maintainable, with clear separation between:

* UI
* Database
* HandBrake integration
* File system operations
* Transfer management
* Verification
* Settings
* Logging

Before implementing destructive source replacement, complete and test the non-destructive MVP first.
