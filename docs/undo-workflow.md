# Guarded replacement undo

Undo reverses a completed replacement through four separately guarded steps. Its strict ordering restores and verifies the original source before the promoted final file can be touched. The verified backup is retained throughout and is never recycled or deleted by undo.

## 1. Prepare undo

The desktop shows the source, backup, and promoted-final paths and requires explicit confirmation. Preparation is database-only. It requires a completed replacement, empty source and temporary paths, and matching SHA-256 digests for the backup and promoted final file before advancing `Completed` to `UndoPrepared`.

## 2. Restore the original source

After separate confirmation, restoration streams the verified backup into a deterministic `.hbcm-restoring` artifact beside the original source path. A matching partial artifact can resume only after every retained byte is compared with the backup. The completed artifact is flushed, SHA-256 verified, and atomically renamed to the unoccupied source path before `SourceRestored` is recorded.

The promoted final file remains protected and unchanged during restoration.

## 3. Recycle the promoted final file

After the restored source and backup are re-verified, a separate confirmation may move the promoted final file to the Windows Recycle Bin. The forced-recycle adapter fails instead of falling back to permanent deletion. Intent is recorded before the Recycle Bin operation and `FinalRecycled` afterward.

Recovery distinguishes an intent with the final still present from a completed removal whose checkpoint was interrupted. It never repeats removal when the final path is already empty.

## 4. Complete undo

The final step verifies the restored source and backup again, requires the promoted-final and temporary paths to remain empty, then uses one SQLite transaction to:

- Advance `FinalRecycled` to `Undone` with the expected journal revision.
- Restore source availability in completed history.

The replacement operation remains a durable record of the completed-and-undone transaction. Repeating undo completion is idempotent.

## Restart recovery

Every non-terminal undo checkpoint appears in the Recovery overview, including undo work attached to an otherwise completed replacement operation. Opening the recovery item selects the matching history record and enables only the action legal for its current checkpoint. `Completed` and `Undone` are terminal, so neither appears as incomplete recovery work.

## Safety summary

- Source restoration always precedes promoted-final recycling.
- No path is overwritten automatically.
- Both removal steps use only the Windows Recycle Bin.
- The verified backup is retained after forward completion and after undo.
- Locked, missing, altered, stale, or ambiguous artifacts stop continuation.
