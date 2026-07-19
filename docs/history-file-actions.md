# History File Actions

The completed-history view provides non-destructive actions for the source and converted files associated with each record.

## Available actions

Select a history row to enable:

- **Play output** and **Play source**, which open the selected file with its Windows default application.
- **Show output** and **Show source**, which open File Explorer and select the corresponding file.
- **Copy output path** and **Copy source path**, which place the full stored path on the clipboard.

Double-clicking a history row opens the converted file with its Windows default application.

## Missing files

Playback and Explorer actions verify that the file currently exists. If it has been moved or deleted, the application reports the missing path without launching another process. Copy Path remains available because a stored path can still be useful for investigation.

These actions never modify, move, replace, or delete either file. File deletion and Remove from History remain separate workflows.
