# Replacement safety preflight

Phase 2 begins with a review-only replacement preflight. It calculates the intended paths, examines current file metadata, reports blocking conditions and warnings, and displays the plan without changing any file.

## Open the review

Select a completed encode and choose **Review replacement**. The review displays:

- Current source and converted paths and sizes.
- Planned final path in the source directory, using the source base name and converted extension.
- Temporary-copy path with the `.hbcm-copying` suffix.
- Original-backup path under `HandBrake Original Backup` in the source directory.
- Every blocking issue and warning found from the current file-system state.

The window is deliberately review-only. It has no command that copies, moves, renames, backs up, replaces, or deletes a file.

## Blocking checks

A future replacement cannot start when:

- The source or converted file is missing.
- Either file is empty or its size cannot be read.
- The source and converted paths refer to the same file.
- The source and converted extensions are the same.
- A file or directory already occupies the planned final path.
- A previous temporary-copy path already exists.
- The current source size differs from the size recorded at encode completion.
- The current converted size differs from the size recorded at encode completion.
- The converted file modification time differs from the time recorded at encode completion.

A converted file larger than the source produces a warning rather than silently appearing safe.

## Persistent recovery state

Database migration `002_replacement_operations.sql` introduces durable operation records for later execution work. Each record stores:

- The related completed-encode record.
- Planned source, converted, final, temporary, and backup paths.
- Source and converted sizes.
- Operation status and current stage.
- Bytes copied.
- Verification state.
- Failure details and update timestamps.

Defined stages cover preparing, copying, verifying, backing up the source, finalising, completion, failure, and cancellation. Incomplete records can be queried after restart so later execution code does not have to guess whether a partial file is valid.

SQLite constraints reject unknown state values, invalid file sizes, negative or oversized byte progress, and more than one active operation for the same completed encode.

## Temporary-copy foundation

The backend can now create a new `.hbcm-copying` file by streaming the converted output, persisting progress, and verifying the completed copy with its size and SHA-256 digest. It re-runs preflight immediately before copying and confirms that the converted file remains unchanged while it is read.

This capability is not exposed in the interface. Cancellation and failures leave the temporary file and durable operation state available for later recovery decisions; they do not automatically delete evidence of an interrupted operation. See [Verified temporary copy](temporary-copy-engine.md).

## Still disabled

Passing preflight does not enable source replacement. The following must be implemented and tested before any destructive transition is available:

1. User-facing execution, progress, and cancellation controls.
2. Original backup with conflict-safe naming.
3. Atomic finalisation.
4. Restart recovery, retry, and cleanup decisions.
5. Undo and restore-original behavior.
6. Explicit final user confirmation.

The mandatory rule remains: never remove or archive the source until the converted file has been copied to the source location and verified.
