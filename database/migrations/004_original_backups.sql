PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS replacement_backups (
    operation_id TEXT PRIMARY KEY NOT NULL,
    backup_path TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('Planned', 'Copying', 'Verifying', 'Verified', 'Failed', 'Cancelled')),
    source_size INTEGER NOT NULL CHECK (source_size > 0),
    bytes_copied INTEGER NOT NULL DEFAULT 0 CHECK (bytes_copied >= 0 AND bytes_copied <= source_size),
    sha256 TEXT NULL,
    failure_message TEXT NULL,
    date_created_utc TEXT NOT NULL,
    date_updated_utc TEXT NOT NULL,
    FOREIGN KEY (operation_id) REFERENCES replacement_operations (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_replacement_backups_status
    ON replacement_backups (status, date_updated_utc DESC);
