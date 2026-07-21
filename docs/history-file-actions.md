# Completed History Browsing

The completed-history view provides search, filtering, sorting, record details, and non-destructive actions for the source and converted files associated with each record.

## Search and quick filters

Search accepts multiple terms and matches them case-insensitively across source and output filenames, full paths, extensions, and record status. Every entered term must match at least one field.

Quick filters provide:

- All records
- Today, based on the current Windows time zone
- Last 7 days
- Records whose source or output was missing when captured
- Records whose output is larger than the source

The visible-result count updates whenever search text, a quick filter, or history data changes. Clear resets both search and the quick filter.

## Sorting and details

The first column numbers the currently displayed rows from 1 in their active filtered and sorted order. Select any sortable column heading to reorder the visible records. Date, file-size, percentage, and storage-saved columns use their raw values, so sorting is numeric or chronological rather than alphabetical.

All cells use horizontal and vertical padding to keep text away from row and column edges. The **Output Size** percentage is pale orange when the converted file exceeds 80% of the original size and pale red when it reaches 90% or more.

Selecting one row displays its completion time, file-action status, full source and output paths, size comparison, exit code, and current file availability. Automatic refresh first compares the latest database result with the displayed rows. It does not rebuild an unchanged list, and selected records remain selected when changed data is applied and they still match the active filter.

Use Ctrl-click to add or remove individual rows, Shift-click to select a range, or Ctrl+A while the table is focused to select every shown row. **Select all shown** applies the current search and quick filter; **Clear selection** removes the selection. Playback, Explorer reveal, and the detailed recovery window remain single-record actions.

The **Status** column is reserved for file-management outcomes rather than repeating HandBrake's completed state. It displays **Output Deleted**, **Source Replaced**, or **Source Replaced, Output Kept**. Untouched records leave this field blank.

## Available actions

Select a history row to enable:

- **Play output** and **Play source**, which open the selected file with its Windows default application.
- **Show output** and **Show source**, which open File Explorer and select the corresponding file.
- **Recycle output**, which uses the guarded workflow described below.

Double-clicking a history row opens the converted output with its Windows default application. After a default source replacement consumes that output, double-click opens the replacement file in the source library instead.

## Missing files

Playback and Explorer actions verify that the file currently exists. If it has been moved or deleted, the application reports the missing path without launching another process.

Playback, Explorer reveal, and Remove from History remain separate from output recycling.

## Recycle output

**Recycle output** is intended for a converted file that does not meet expectations. A path-specific warning requires explicit confirmation and states that the source and completed-history record will be retained.

Before asking Windows to recycle the file, the application:

- Confirms the stored source and output are different paths.
- Refuses a missing output or incomplete captured metadata.
- Refuses while unfinished replacement work still depends on that output.
- Opens a protected read handle and revalidates the captured file size and last-write time, preventing a changed or unrelated file from being removed.

The application invokes the Windows Recycle Bin with no permanent-delete fallback. After success it marks only the output availability flag as unavailable; the source and full history record remain. If verification, locking, or recycling fails, the operation stops safely and leaves the history state unchanged.

## Remove from history

**Remove from history** deletes the selected SQLite history record and any internal legacy recovery metadata that belongs exclusively to it. A styled confirmation identifies the exact source and output paths and states that neither media file will be deleted or changed. Cancelling the confirmation leaves the record and both files untouched.

After a confirmed removal, SQLite work runs away from the interface thread so the confirmation can close smoothly while the history table, active filter results, summary totals, and details selection update. This action cannot be used to delete source or output files.

## Bulk actions

When more than one row is selected, the primary actions change to **Replace selected**, **Recycle outputs**, and **Remove history** with the selected count.

Every bulk action opens a scrollable confirmation window listing every selected source and output or target path. Initially blocked items remain visible and are skipped. Eligible items run one at a time, never in parallel. **Stop after current** allows the active verified item to reach a safe boundary before the remaining items are skipped.

Bulk source replacement uses the same direct move/copy behavior as the single-record workflow. The user chooses whether outputs are consumed or kept, and cross-selection conflicts resolving to the same final path are blocked before confirmation. After the one bulk confirmation, one persistent progress window lists every eligible item. Each item has its own byte-progress bar and status, while an overall item-based bar and `processed/total` counter advance after each attempt. **Stop after current** finishes the active item safely; **Cancel current & stop** is enabled during a cancellable copy.

Bulk output recycling revalidates each output immediately before invoking the Windows Recycle Bin and never falls back to permanent deletion. Bulk history removal changes only SQLite records. After confirmation closes, bulk history removal opens a separate live progress window before processing begins; the window remains responsive, identifies the current item, and stays visible with the final removed, failed, and skipped totals. Each workflow ends with succeeded, failed, and skipped totals.
