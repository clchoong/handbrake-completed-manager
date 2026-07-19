# Database Migrations

This directory contains the versioned SQLite schema used by HandBrake Completed Manager.

## Current migration

- `001_initial.sql` creates the `completed_encodes` table, its unique event fingerprint, and indexes for completion time, source path, and destination path.

The Infrastructure project embeds the migration as an assembly resource. `CompletedEncodeRepository.InitializeAsync` applies it idempotently when the history database is opened.

Future schema changes must be added as new numbered migrations. Existing migrations must remain immutable after release so established databases can be upgraded predictably.
