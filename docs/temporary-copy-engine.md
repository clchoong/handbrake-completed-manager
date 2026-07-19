# Verified temporary-copy engine

The Phase 2 backend can safely prepare a converted file for a future replacement operation. It creates a separate temporary file beside the planned final file and never changes the source file, converted output, or final path.

This engine is currently an internal foundation. It is not available from the application interface and does not perform source backup or replacement.

## Copy workflow

Before writing, the engine:

1. Re-runs replacement preflight against the current file system.
2. Rejects missing, changed, conflicting, or unsafe paths.
3. Creates a durable planned-operation record.
4. Confirms that the destination volume has enough available space.
5. Opens the temporary path with create-new behavior, so an existing partial file is never overwritten.

The converted output is streamed in one-megabyte blocks. Byte progress is reported to the caller and periodically saved in SQLite. After the stream is flushed to disk, the engine verifies the temporary file's length and SHA-256 digest. It also checks that the converted file's size and modification time remained stable during the copy.

Successful verification leaves the operation at the verified preparation boundary. Later milestones must implement original backup and atomic finalisation before replacement can complete.

## Cancellation and failures

Cancellation and copy or verification failures are written to the durable operation record. Any partial `.hbcm-copying` file is deliberately retained for a future recovery screen to inspect, retry, or clean up explicitly.

The engine does not automatically delete a partial file because an interrupted process may otherwise lose the evidence needed to make a safe recovery decision. A later attempt also refuses to overwrite that path.

## Safety boundary

The temporary-copy engine never:

- Deletes, renames, moves, or edits the original source.
- Deletes, renames, moves, or edits the converted HandBrake output.
- Creates or overwrites the planned final file.
- Creates the original-backup file.
- Treats a partial or unverified temporary file as complete.

The mandatory replacement rule remains: the original source cannot be archived or removed until the converted content has been copied into the source location and verified.
