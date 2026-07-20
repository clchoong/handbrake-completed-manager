# Verified source restoration

Source restoration is the first executable undo step. It reconstructs a missing original source from the verified backup before any later action may touch the promoted final file. The service is implemented and safety-tested as a backend recovery boundary; it is not yet exposed in the desktop interface because source recycling remains disabled.

## Preconditions

Restoration is accepted only when:

- The transaction is at `UndoPrepared` or `RestoreSourceIntentRecorded`.
- The original source path is unoccupied.
- The backup and promoted final paths are distinct from the source and from each other.
- The replacement operation remains at its verified boundary.
- The backup length and SHA-256 match the prepared source digest.
- The promoted final length and SHA-256 match the prepared final digest.

An existing file or directory at the source path is never overwritten, even if it appears to contain the expected bytes.

## Copy and promotion sequence

The restore artifact has a deterministic name beside the source:

```text
<source-path>.<operation-id>.hbcm-restoring
```

The service protects the backup and final file with read-only locks, records `RestoreSourceIntentRecorded`, then streams the backup into the restore artifact. It flushes the copy to disk, verifies its full SHA-256 digest, and atomically renames it to the unoccupied source path. Only then does it record `SourceRestored`.

The promoted final and verified backup are never moved, renamed, truncated, or deleted by restoration.

## Interruption recovery

- Intent recorded with no restore artifact: restart the copy from the verified backup.
- Matching partial restore artifact: compare every retained byte with the backup, append the remainder, and verify the completed digest.
- Fully verified restore artifact: reverify it and perform the atomic rename.
- Source present after the atomic rename but before the completion checkpoint: verify the restored source, backup, and final file, then record `SourceRestored`.
- Mismatched, oversized, locked, or ambiguous artifacts: refuse automatic continuation and retain the restore artifact for review.

Failures after intent are written to the revisioned transaction journal. Recovery never deletes a questionable partial artifact and never modifies the final file.

## Remaining safety boundary

No desktop command currently starts undo or source restoration. Source recycling, final-file recycling, and transaction completion remain disabled. The next forward milestone must connect source retirement only after an explicit confirmation and must prove that this restoration path can recover every source-retirement crash boundary.
