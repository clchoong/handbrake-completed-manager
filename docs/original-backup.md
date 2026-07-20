# Original-backup preparation

After the converted temporary copy has passed size and SHA-256 verification, the replacement review can create a separate verified copy of the original source under `HandBrake Original Backup` in the source directory.

This is a non-destructive preparation stage. The source is opened read-only and remains at its original path. The application does not move, rename, truncate, replace, recycle, or delete it.

## Preconditions

Backup preparation requires:

- The latest replacement operation still matches the reviewed encode and every planned path.
- The operation is in progress at the verified temporary-copy boundary.
- The verified `.hbcm-copying` file still exists at its recorded size.
- The source still exists at its recorded size.
- The planned final path and original-backup path are unoccupied.
- The backup volume has enough available space for the complete source.

The backup file is opened with create-new behavior, so an existing file or directory is never overwritten. The review displays the exact backup path and requires a separate confirmation.

## Copy and verification

The source is streamed to the backup in one-megabyte blocks. Progress is displayed and periodically persisted independently from temporary-copy progress. The source is held with a read share that refuses concurrent writers while it is copied.

After the backup is flushed to disk, the application verifies:

- Copied bytes and backup length equal the recorded source size.
- Source and backup SHA-256 digests match.
- Source size and modification time remained stable during copying and verification.

Database migration `004_original_backups.sql` stores backup path, status, byte progress, SHA-256, failure details, and timestamps separately from converted-copy verification.

## Cancellation and recovery

Cancellation or failure is recorded durably. Any partial backup remains visible for recovery review and is never treated as verified. The replacement operation returns to the verified temporary-copy boundary so the partial backup can be reviewed without invalidating the converted copy.

The user may permanently discard a verified or partial backup artifact after a confirmation showing its exact path. Cleanup validates the reviewed paths and latest database state, obtains an exclusive lock, and deletes only the recorded backup artifact. It never changes the source, converted output, verified temporary copy, or planned final path.

After cleanup, backup preparation may be retried. A locked, mismatched, stale, missing, or directory path is refused.

## Still disabled

Creating a verified backup does not authorize source replacement. The application still cannot:

- Move or delete the original source.
- Restore or expire backups automatically.
- Complete or undo a replacement.

A separate read-only readiness review can now verify the persisted operation and both file pairs again. Passing that review reports only that the prerequisites are currently consistent; it does not enable or perform a transition. See [Finalisation readiness and restart recovery](finalization-readiness.md).

Atomic promotion is now available as a separate, guarded step and leaves the source untouched. Source recycling, backup restoration, completion, and undo still require their own executors, tests, and explicit confirmations.
