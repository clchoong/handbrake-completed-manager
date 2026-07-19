# Verified temporary-copy engine

The Phase 2 backend can safely prepare a converted file for a future replacement operation. It creates a separate temporary file beside the planned final file and never changes the source file, converted output, or final path.

The engine is available from **Review replacement** after preflight succeeds. It does not perform source backup or replacement.

## User workflow

The review window displays the exact temporary path and requires explicit confirmation before copying. During the operation it shows byte and percentage progress and provides a cancellation command. Closing the window during an active copy asks whether to cancel, waits for the operation to stop, and then closes.

Success displays the verified byte count and SHA-256 digest. Cancellation or failure clearly states that original files were unchanged and that any partial temporary file remains for recovery review.

Opening the review again shows the latest durable operation state. A partial file or incomplete operation prevents another attempt from overwriting recovery evidence. A failed attempt that created no partial file can be retried after the underlying problem is fixed.

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
