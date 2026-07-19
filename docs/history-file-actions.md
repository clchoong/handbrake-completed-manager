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

Select any column heading to sort the visible records. Date, file-size, percentage, and storage-saved columns use their raw values, so sorting is numeric or chronological rather than alphabetical.

Selecting a row displays its completion time, status, full source and output paths, size comparison, exit code, and captured file-availability state. The selected record remains selected during automatic history refresh when it still matches the active filter.

## Available actions

Select a history row to enable:

- **Play output** and **Play source**, which open the selected file with its Windows default application.
- **Show output** and **Show source**, which open File Explorer and select the corresponding file.
- **Copy output path** and **Copy source path**, which place the full stored path on the clipboard.

Double-clicking a history row opens the converted file with its Windows default application.

## Missing files

Playback and Explorer actions verify that the file currently exists. If it has been moved or deleted, the application reports the missing path without launching another process. Copy Path remains available because a stored path can still be useful for investigation.

These actions never modify, move, replace, or delete either file. File deletion and Remove from History remain separate workflows.

## Remove from history

**Remove from history** deletes only the selected SQLite history record. A warning confirmation identifies the source and output filenames and states that neither file will be deleted or changed. Cancelling the confirmation leaves the record and both files untouched.

After a confirmed removal, the history table, active filter results, summary totals, and details selection update immediately. This action cannot be used to delete source or output files.
