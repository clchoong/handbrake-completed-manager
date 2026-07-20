# Finalisation readiness and restart recovery

The application provides a read-only safety gate after the converted temporary copy and original-backup copy are verified. It also reviews persisted incomplete operations when the desktop application starts.

Neither feature moves, renames, replaces, truncates, recycles, or deletes the source. Passing the readiness gate does not authorize finalisation.

## Readiness review

The replacement window enables **Check finalisation readiness** only when the latest operation is in progress at the original-backup stage and both artifacts are persisted as verified.

The review requires:

- The operation, completed encode, backup state, and every recorded path to match the current plan.
- The source, converted output, temporary copy, and original-backup copy to exist at their recorded sizes.
- Persisted byte progress to be complete for both copies.
- The planned final path to remain unoccupied.
- The converted output and temporary copy to have identical SHA-256 digests.
- The source and original-backup copy to have identical SHA-256 digests.
- The source digest to match the digest stored when the backup was verified.
- Every file's length and modification time to remain stable throughout the complete four-file review.

Files are opened read-only while they are hashed, with concurrent writers refused during each read. If any file changes, becomes unavailable, or fails a check, the review reports blocking reasons and no transition is offered.

## Restart-recovery overview

At startup and after manual refresh or a replacement review, the main window scans non-completed replacement operations and their current artifacts. The **Recovery** counter opens a read-only overview with a suggested next review action:

- Retry temporary-copy preparation when a failed or cancelled operation has no retained artifact.
- Review and discard an incomplete temporary or backup artifact.
- Continue to original-backup preparation after a verified temporary copy.
- Run the read-only finalisation readiness check after both artifacts are verified.
- Perform manual review when persisted state and current artifacts are inconsistent.

Operations whose artifacts were explicitly discarded and whose cancellation state records that confirmation are omitted. The overview never retries, resumes, or cleans up automatically; recovery actions remain in the record-specific replacement review with their existing confirmations and path guards.

## Current safety boundary

When the integrity review passes, the application can persist a `Prepared` transaction journal containing the verified source and final-file SHA-256 digests. This changes only the local SQLite database and still does not authorize a file transition.

The prepared temporary copy may now be promoted through the separately confirmed [atomic final-file promotion](atomic-final-promotion.md) step. Promotion leaves the original source and verified backup untouched.

Source recycling and completed replacement remain disabled. The application also has no command that restores the source, recycles the promoted final file, or performs undo. The checkpoint design and remaining crash-boundary rules are documented in [Finalisation transaction design](finalization-transaction-design.md).
