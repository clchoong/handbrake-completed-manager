# Database Migrations

This directory contains the versioned SQLite schema used by HandBrake Completed Manager.

## Current migrations

- `001_initial.sql` creates the `completed_encodes` table, its unique event fingerprint, and indexes for completion time, source path, and destination path.
- `002_replacement_operations.sql` adds persistent replacement stages, paths, byte progress, verification state, and failure details for safe interruption recovery.

The Infrastructure project embeds both migrations as assembly resources. `CompletedEncodeRepository.InitializeAsync` applies them in order and idempotently when the history database is opened.

Future schema changes must be added as new numbered migrations. Existing migrations must remain immutable after release so established databases can be upgraded predictably.
