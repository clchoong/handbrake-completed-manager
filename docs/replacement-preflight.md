# Replacement safety preflight

> Legacy workflow: this document describes verified replacement operations created by versions before 0.8.0. The normal 0.8.0 **Replace Source** action uses the direct move/copy workflow documented in [Direct source replacement](one-click-safe-replacement.md).

Phase 2 begins with a replacement preflight. It calculates the intended paths, examines current file metadata, reports blocking conditions and warnings, and displays the plan before any temporary copy can start.

## Open the review

Select a completed encode and choose **Review replacement**. The review displays:

- Current source and converted paths and sizes.
- Planned final path in the source directory, using the source base name and converted extension.
- Temporary-copy path with the `.hbcm-copying` suffix.
- Original-backup path under `HandBrake Original Backup` in the source directory.
- Every blocking issue and warning found from the current file-system state.

When preflight passes, the window can create and verify a separate temporary file after explicit user confirmation. After that succeeds, a second confirmation can create and verify a separate original-backup copy. Preflight itself never moves, renames, replaces, or deletes the source or converted file; later transitions require their own integrity gates and confirmations.

## Blocking checks

Replacement preparation cannot start when:

- The source or converted file is missing.
- Either file is empty or its size cannot be read.
- The source and converted paths refer to the same file.
- A file or directory already occupies the planned final path.
- A previous temporary-copy path already exists.
- A file or directory already occupies the planned original-backup path.
- The current source size differs from the size recorded at encode completion.
- The current converted size differs from the size recorded at encode completion.
- The converted file modification time differs from the time recorded at encode completion.

A converted file larger than the source produces a warning rather than silently appearing safe.

When source and output extensions match, the planned final path is the original source path. That existing path is expected and is not treated as an unrelated-file conflict. The guarded workflow verifies the converted temporary copy and original backup before using the Windows atomic file-replacement operation.

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

The review window exposes this capability with the exact temporary path, explicit confirmation, live byte progress, and a cancellation command. Cancellation and failures leave the temporary file and durable operation state available for later recovery decisions; they do not automatically delete evidence of an interrupted operation. See [Verified temporary copy](temporary-copy-engine.md).

When the review is opened again, it displays the latest related operation. An incomplete operation or any existing temporary file blocks a new copy. A failed or cancelled attempt with no remaining partial file is reported but may be retried after its cause is resolved.

If a recorded temporary file exists, **Discard temporary file** provides an explicit recovery cleanup. The confirmation shows the exact path and states that deletion is permanent. Cleanup proceeds only when the operation still matches the reviewed encode and paths, remains the latest database state, is not completed, and the file can be opened exclusively. It never targets the source, converted output, final path, or backup path. After cleanup, preflight runs again and enables retry only if every current check passes.

## Execution boundary

Passing preflight authorizes only temporary-copy preparation. Original backup, readiness, promotion, source recycling, completion, and undo are separate guarded stages documented elsewhere. The mandatory rule remains: never remove or archive the source until the converted file has been copied to the source location and verified.
