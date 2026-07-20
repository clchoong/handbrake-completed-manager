# One-click safe source replacement

For a normal completed encode, the user selects **Replace source**, reviews one warning containing the exact source and converted-output paths, and selects **Replace source** again to authorize the complete guarded replacement. A dedicated progress window then shows one familiar overall progress bar, the current stage, and the active file operation until completion or a recoverable stop.

This removes the need to understand or approve each internal stage. The exact planned final and backup paths remain in the warning text, while detailed transaction controls are shown only when the user opens Recovery for an interrupted operation.

## Automated sequence

The coordinator runs the existing safety services in this fixed order:

1. Re-run preflight against the current filesystem and refuse new work when recovery is blocking.
2. Copy the HandBrake output to the deterministic temporary path and verify its size and SHA-256 digest.
3. Copy the original source to the deterministic backup path and verify its size and SHA-256 digest.
4. Revalidate every persisted state, path, size, timestamp, and digest.
5. Persist the finalisation journal and atomically promote the temporary copy. For differing extensions, this creates an unoccupied final path; for matching extensions, Windows atomically replaces the verified original path.
6. Revalidate the source, backup, and promoted final. When the final path differs, move the original source to the Windows Recycle Bin; an in-place replacement instead retains the verified original backup for recovery.
7. Verify the final and backup again and atomically complete the database transaction.

The original HandBrake output remains at its recorded output path. The promoted copy occupies the planned source-library filename, including the original path when both extensions match. The verified original backup remains available for recovery.

## Failure and recovery behavior

Before the finalisation journal is created, a copy or verification failure retains the existing non-destructive recovery artifacts and operation state. Once the journal exists, each filesystem mutation is preceded by an intent checkpoint. A process interruption or Windows Recycle Bin failure therefore leaves a classifiable checkpoint for the Recovery view.

The workflow never:

- Overwrites an unrelated occupied final, temporary, or backup path.
- Permanently deletes the original source.
- Removes the source before the converted copy and original backup are independently verified.
- Treats a missing file as a successful transition without the matching persisted intent.

If automatic execution stops, the progress window reports the error and directs the user to Recovery. The checkpoint-specific detailed control can continue the transaction without repeating already completed destructive work.
