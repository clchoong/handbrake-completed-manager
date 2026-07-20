# Database Migrations

This directory contains the versioned SQLite schema used by HandBrake Completed Manager.

## Current migrations

- `001_initial.sql` creates the `completed_encodes` table, its unique event fingerprint, and indexes for completion time, source path, and destination path.
- `002_replacement_operations.sql` adds persistent replacement stages, paths, byte progress, verification state, and failure details for safe interruption recovery.
- `003_replacement_retry_index.sql` allows a corrected retry after a failed attempt while continuing to prevent concurrent planned or in-progress operations for the same encode.
- `004_original_backups.sql` stores independent original-backup progress, verification hashes, failures, cancellation, and recovery timestamps.

The Infrastructure project embeds all migrations as assembly resources. `CompletedEncodeRepository.InitializeAsync` applies them in order and idempotently when the history database is opened.

Future schema changes must be added as new numbered migrations. Existing migrations must remain immutable after release so established databases can be upgraded predictably.
