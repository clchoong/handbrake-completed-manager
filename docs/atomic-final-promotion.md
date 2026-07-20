# Atomic final-file promotion

Atomic promotion renames the verified `.hbcm-copying` artifact to its planned final filename in the same source directory. It does not move, rename, recycle, truncate, or delete the original source or its verified backup. The original converted output also remains unchanged.

## User confirmation

Promotion is available only after a successful readiness review has persisted a `Prepared` transaction. The replacement review displays the exact temporary, final, and original-source paths and requires an explicit **Yes** confirmation. **No** is the default.

## Protected operation

Immediately before promotion, the service reloads the operation and transaction from SQLite and requires the verified preparation boundary. It then:

1. Confirms the temporary and final paths have the same parent directory.
2. Refuses an existing file or directory at the final path.
3. Opens the source and backup read-only while denying writers and deletion.
4. Opens the temporary copy read-only while denying writers but allowing its atomic rename.
5. Rechecks the recorded lengths and SHA-256 digests of all three protected files.
6. Rechecks that the final path remains unoccupied.
7. Persists `PromoteTemporaryIntentRecorded` with an optimistic revision guard.
8. Renames the temporary copy with non-overwriting `File.Move` semantics.
9. Persists `FinalPromoted`.

The final-path check and rename are intentionally non-overwriting. If another process creates the final path during the operation, the rename fails and that path is left untouched.

Once intent is recorded, the window cannot cancel or close the operation. The rename itself is a same-directory metadata transition; the potentially slow hashing occurs before intent is written.

## Interruption recovery

Failure details are persisted at the current intent checkpoint with a new revision.

- If the temporary file still exists and the final path is empty, recovery locks and verifies all protected artifacts again before retrying the rename.
- If the temporary path is empty and the final file matches the prepared digest, recovery verifies the source and backup and records `FinalPromoted` without repeating the rename.
- If both paths are occupied, neither exists, a protected file is locked, or any digest differs, automatic continuation is refused.

The main Recovery overview identifies operations with a finalisation journal and directs them to record-specific review.

## Remaining safety boundary

After promotion, the source and backup remain present and verified. The desktop may then offer [Guarded source recycling](source-recycling.md) as a separate, explicitly confirmed Windows Recycle Bin operation. Replacement completion, final-file recycling, and desktop undo execution remain disabled.
