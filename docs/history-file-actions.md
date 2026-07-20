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

Selecting a row displays its completion time, status, source-replacement state, full source and output paths, size comparison, exit code, and captured file-availability state. The selected record remains selected during automatic history refresh when it still matches the active filter.

The **Source replacement** column reports **Not replaced**, **In progress**, **Needs attention**, **✓ Replaced**, or **Restored** from the latest durable replacement operation and finalisation checkpoint. The check mark appears only after forward replacement has completed atomically.

## Available actions

Select a history row to enable:

- **Play output** and **Play source**, which open the selected file with its Windows default application.
- **Show output** and **Show source**, which open File Explorer and select the corresponding file.
- **Recycle output**, which uses the guarded workflow described below.

Double-clicking a history row opens the converted file with its Windows default application.

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

**Remove from history** deletes only the selected SQLite history record. A warning confirmation identifies the source and output filenames and states that neither file will be deleted or changed. Cancelling the confirmation leaves the record and both files untouched.

After a confirmed removal, the history table, active filter results, summary totals, and details selection update immediately. This action cannot be used to delete source or output files.
