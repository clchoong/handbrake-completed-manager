# HandBrake Activity-Log Import

HandBrake Completed Manager can recover completed-history records from the activity logs that HandBrake retains under the current Windows profile. This is a fallback for encodes completed before the receiver was configured or while it was unavailable; it does not restore HandBrake's Queue.

## Default location

The **Import logs** action reviews:

```text
%APPDATA%\HandBrake\logs
```

The folder is read only. Import does not rename, move, edit, or delete any HandBrake log or media file and does not change HandBrake preferences.

## Recovery requirements

A log is eligible only when all of the following are true:

- HandBrake recorded `libhb: work result = 0`.
- The log contains the completed-job marker.
- The JSON job section contains valid absolute source and destination paths.
- The destination file still exists when the log is reviewed.
- The calculated completion fingerprint is not already in completed history or another reviewed log.

Paused, unfinished, failed, malformed, duplicate, and missing-output logs remain visible in the review with the reason they will be skipped. Missing source files do not prevent import because preserving their last known path is still useful; the imported record is marked **Source Missing**.

The completion time comes from HandBrake's `Finished work at` line. If that line cannot be parsed, the log file's last-write time is used as a conservative fallback.

## Workflow

1. Choose **Import logs** in the application header.
2. Review every detected source and output path and its eligibility status.
3. Confirm the import count.
4. The application rechecks each output immediately before inserting its record.
5. Completed history reloads and reports imported, duplicate, skipped, and failed totals.

Running the importer again is safe. Existing records are deduplicated rather than inserted a second time.
