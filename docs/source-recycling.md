# Guarded source recycling

Source recycling is the first recoverable removal step in forward finalisation. It becomes available only after the converted temporary copy has been atomically promoted and the verified original backup remains available. The action removes the original source from its path through the Windows Recycle Bin; it never requests permanent deletion.

## Explicit confirmation

The replacement review shows the exact source, promoted final, and verified backup paths. A separate **Yes/No** warning explains that the source path will become empty, Windows will retain the source in the Recycle Bin, and failure to guarantee recycling stops the operation. **No** is the default.

The action cannot be cancelled or the window closed after execution starts because the journal and filesystem must reach a classifiable crash boundary.

## Protected operation

Immediately before recycling, the service reloads the operation and transaction from SQLite and requires `FinalPromoted` or an interrupted `RecycleSourceIntentRecorded` checkpoint. It then:

1. Confirms the source, backup, and final paths are distinct.
2. Opens the source read-only, denying writers while permitting its Recycle Bin transition.
3. Opens the backup and final file read-only, denying writers and removal.
4. Rechecks the prepared lengths and SHA-256 digests of all three files.
5. Persists `RecycleSourceIntentRecorded` with an optimistic revision guard.
6. Uses Windows `IFileOperation` with `FOFX_RECYCLEONDELETE` and early-failure behavior.
7. Confirms the source path is unoccupied.
8. Persists `SourceRecycled`.

The Windows adapter suppresses shell confirmation because the application already obtained an explicit path-specific confirmation. If Windows cannot use the Recycle Bin, the operation fails; it does not fall back to permanent deletion.

## Interruption recovery

- If intent is recorded and the source remains present, all three protected files are locked and verified again before retrying the Recycle Bin operation.
- If intent is recorded and the source path is empty, the backup and promoted final file are verified before recording `SourceRecycled` without repeating the removal.
- A missing source without a prior intent checkpoint is never interpreted as successful recycling.
- Locked, altered, missing, or ambiguous protected artifacts stop automatic continuation and persist a recovery message.

The verified backup and promoted final file are never moved, renamed, truncated, recycled, or deleted by this operation. Verified source restoration remains the mandatory first file transition during later undo.

## Remaining safety boundary

After `SourceRecycled`, [Forward completion and restart continuation](forward-completion-and-recovery.md) performs a final read-only integrity check and atomically closes the SQLite transaction. A separately confirmed [guarded undo](undo-workflow.md) may later restore the source and recycle the promoted final.
