PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS completed_encodes (
    id TEXT PRIMARY KEY NOT NULL,
    event_fingerprint TEXT NOT NULL UNIQUE,
    completed_at_utc TEXT NOT NULL,
    source_path TEXT NOT NULL,
    source_filename TEXT NOT NULL,
    source_extension TEXT NOT NULL,
    source_size INTEGER NULL,
    source_exists INTEGER NOT NULL,
    destination_path TEXT NOT NULL,
    destination_filename TEXT NOT NULL,
    destination_extension TEXT NOT NULL,
    destination_size INTEGER NULL,
    destination_exists INTEGER NOT NULL,
    destination_last_write_utc TEXT NULL,
    output_percentage REAL NULL,
    space_saved_percentage REAL NULL,
    space_saved_bytes INTEGER NULL,
    exit_code INTEGER NOT NULL,
    current_status TEXT NOT NULL,
    date_created_utc TEXT NOT NULL,
    date_updated_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_completed_encodes_completed_at
    ON completed_encodes (completed_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_completed_encodes_source_path
    ON completed_encodes (source_path);

CREATE INDEX IF NOT EXISTS ix_completed_encodes_destination_path
    ON completed_encodes (destination_path);

