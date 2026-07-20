# One-click safe source replacement

After reviewing a completed encode, its exact source, converted-output, planned-final, temporary, and backup paths, the user can select **Replace source safely**. One path-specific **Yes/No** confirmation authorizes the complete guarded replacement; **No** remains the default.

This removes the need to manually cut and paste files or approve each internal stage separately. The detailed stage controls remain available for recovery and technical review.

## Automated sequence

The coordinator runs the existing safety services in this fixed order:

1. Re-run preflight against the current filesystem and refuse new work when recovery is blocking.
2. Copy the HandBrake output to the deterministic temporary path and verify its size and SHA-256 digest.
3. Copy the original source to the deterministic backup path and verify its size and SHA-256 digest.
4. Revalidate every persisted state, path, size, timestamp, and digest.
5. Persist the finalisation journal and atomically promote the temporary copy without overwriting a file.
6. Revalidate the source, backup, and promoted final, then move the original source to the Windows Recycle Bin.
7. Verify the final and backup again and atomically complete the database transaction.

The original HandBrake output remains at its recorded output path. The promoted copy is placed beside the former source under the planned final filename. The verified original backup remains available for later undo.

## Failure and recovery behavior

Before the finalisation journal is created, a copy or verification failure retains the existing non-destructive recovery artifacts and operation state. Once the journal exists, each filesystem mutation is preceded by an intent checkpoint. A process interruption or Windows Recycle Bin failure therefore leaves a classifiable checkpoint for the Recovery view.

The workflow never:

- Overwrites an occupied final, temporary, or backup path.
- Permanently deletes the original source.
- Removes the source before the converted copy and original backup are independently verified.
- Treats a missing file as a successful transition without the matching persisted intent.

If automatic execution stops, the review window reports the error and directs the user to Recovery. The checkpoint-specific detailed control can continue the transaction without repeating already completed destructive work.
