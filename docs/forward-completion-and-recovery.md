# Forward completion and restart continuation

Forward completion closes a replacement transaction after the original source has reached the Windows Recycle Bin. It does not move, recycle, delete, rename, or write any media file.

## Integrity gate

Completion is available only at `SourceRecycled`. Immediately before changing SQLite, the service requires:

- The source path and obsolete `.hbcm-copying` path to be unoccupied.
- The verified original backup to remain present at its prepared length and SHA-256 digest.
- The promoted final file to remain present at its prepared length and SHA-256 digest.
- The source, temporary, backup, and final paths to be distinct.
- The replacement operation to remain in progress at its verified forward boundary.

The backup and final file are held with read-only locks during the complete integrity check and database commit.

## Atomic database completion

One SQLite transaction performs all three state changes:

1. Advances the finalisation journal from `SourceRecycled` to `Completed` using its expected revision.
2. Marks the replacement operation and stage `Completed`.
3. Records that the original source is no longer present in completed history.

If any row is missing, stale, or outside its expected boundary, the entire transaction rolls back. Repeating completion after a successful commit is idempotent: the files are verified again and the existing completed state is returned.

## Restart continuation

The Recovery overview remains read-only on startup; it never mutates a file or transaction automatically. Each incomplete item can now be selected and opened directly. The application selects the matching completed-history record and opens its replacement review, where only the action permitted by the current checkpoint is enabled:

- Recover or retry promotion.
- Recover or retry source recycling.
- Complete a stable `SourceRecycled` transaction.
- Review an ambiguous state without automatic mutation.

After atomic completion, the operation no longer appears in the incomplete Recovery count.

## Remaining safety boundary

The completed promoted final file and verified original backup remain untouched. Desktop undo preparation, source-restoration controls, final-file recycling, and undo completion are separate future actions.
