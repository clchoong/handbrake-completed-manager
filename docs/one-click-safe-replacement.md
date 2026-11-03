# Direct source replacement

The normal **Replace Source** workflow is intentionally direct. It does not create a recovery backup, calculate SHA-256 checksums, or expose the legacy recovery transaction. The confirmation clearly states that the original source cannot be recovered after replacement.

## Choices

- **Replace Source** is the default. It moves the encoded output into the source library and leaves no separate output file.
- **Replace Source and Keep Output** copies the encoded output into the source library and retains the original HandBrake output.

The replacement filename keeps the original source base name and uses the encoded output extension. Matching source and output extensions replace the original path atomically.

## Transfer behavior

When the output can be moved on the same volume, the default operation uses filesystem moves and is normally immediate. A cross-volume replacement must copy bytes because Windows cannot move a file across volumes. Keeping the output also requires a copy.

Copy operations show transferred bytes and percentage. **Cancel** is available during that copy. Cancellation removes the partial transfer, leaves the original source and output unchanged, and permits an immediate retry.

The app performs only a file-length check after copying; it does not read the entire file again for checksum verification.

## Destructive boundary

The source is not removed until the complete output has reached the source directory and passed a file-length check. After that point, the UI disables cancellation and completes the short installation boundary:

1. For matching extensions, Windows atomically replaces the source path.
2. For differing extensions, the prepared file is first renamed to its final, unoccupied source-library path; only then is the original source permanently deleted.
3. The default option removes the separate output if a cross-volume copy was necessary.
4. The completed action is recorded as **Source Replaced** or **Source Replaced, Output Kept**.

The operation refuses the same source/output path and unrelated occupied replacement paths. If a same-volume move fails, it attempts to restore the moved output to its original location. An existing temporary file or an ambiguous occupied final path is never automatically deleted or treated as proof of success: inspect the reported paths before retrying. The direct workflow does not provide a durable crash journal, so an interruption may require manual review. A cancelled copy can be retried after its partial file has been removed.

## Bulk progress

Bulk replacement uses one progress window for the entire confirmed selection. Every eligible file has an individual byte-based progress bar and current stage. The total bar updates continuously: in a five-item batch, 50% progress through the first item produces 10% total progress; completing it produces 20% and changes the counter from `0/5` to `1/5`. Failed and cancelled attempts count as processed so the total reflects how much of the batch has been attempted, while their individual status remains clearly marked.
