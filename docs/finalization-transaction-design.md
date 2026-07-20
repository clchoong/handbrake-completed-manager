# Finalisation transaction design

Finalisation and undo are modelled as revisioned, durable transactions. Atomic temporary-to-final promotion is the only enabled file transition; all source and undo transitions remain disabled.

## Preparation boundary

A transaction can be prepared only when the replacement operation and original-backup records remain at their verified boundaries and the file-read-only readiness review succeeds. Migration `005_finalization_transactions.sql` stores:

- The replacement operation identity.
- The verified original source and future final-file SHA-256 digests.
- A checkpoint and monotonically increasing revision.
- Failure details and creation/update timestamps.

Creating the initial `Prepared` record is guarded by the current operation and backup rows in one SQLite statement. A second preparation with the same digests is idempotent; different digests are refused. Checkpoint updates use the expected checkpoint and revision so stale windows or concurrent recovery attempts cannot both advance the journal.

## Forward ordering

Every future filesystem action has an intent checkpoint written before the action and a completion checkpoint written afterward:

| Stable state | Intent before action | Completed action |
| --- | --- | --- |
| `Prepared` | `PromoteTemporaryIntentRecorded` | `FinalPromoted` |
| `FinalPromoted` | `RecycleSourceIntentRecorded` | `SourceRecycled` |
| `SourceRecycled` | — | `Completed` |

Promotion is designed as a same-directory atomic rename from the verified `.hbcm-copying` path to the unoccupied final path. The original source remains present during promotion. Source recycling is a later, separately confirmed action and can begin only after the promoted final file and verified backup are present. Permanent deletion is not part of this design.

## Undo ordering

Undo prioritises restoring the original before touching the promoted file:

| Stable state | Intent before action | Completed action |
| --- | --- | --- |
| `UndoPrepared` | `RestoreSourceIntentRecorded` | `SourceRestored` |
| `SourceRestored` | `RecycleFinalIntentRecorded` | `FinalRecycled` |
| `FinalRecycled` | — | `Undone` |

If the source is already present and matches its prepared digest, restoration can be skipped. A future restore executor must create and verify the source from the backup before the final file can be recycled.

## Crash-boundary assessment

Recovery never infers success from the journal alone. The read-only assessor hashes the source, temporary, final, and backup paths and compares them with the prepared digests. It distinguishes:

- An intent recorded but the file action not performed, which may be safely retried.
- A file action completed before its completion checkpoint, which may be recorded without repeating the action.
- A stable checkpoint ready for the next separately confirmed action.
- Missing, duplicated, locked, corrupted, or ambiguous artifacts, which require manual review.

The verified backup is mandatory throughout forward and undo recovery. Every legal state-machine edge and both outcomes around each filesystem crash boundary are covered by automated tests. Repository tests cover preparation gates, idempotency, digest mismatch, revision conflicts, skipped checkpoints, and migration upgrades.

## Current execution boundary

The desktop review may advance `Prepared` through `PromoteTemporaryIntentRecorded` to `FinalPromoted` by the guarded process documented in [Atomic final-file promotion](atomic-final-promotion.md). No service recycles the source, restores the source, recycles the final file, or marks finalisation/undo complete.
