PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS replacement_operations (
    id TEXT PRIMARY KEY NOT NULL,
    completed_encode_id TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('Planned', 'InProgress', 'Completed', 'Failed', 'Cancelled')),
    stage TEXT NOT NULL CHECK (stage IN ('Preparing', 'Copying', 'Verifying', 'BackingUpSource', 'Finalizing', 'Completed', 'Failed', 'Cancelled')),
    source_path TEXT NOT NULL,
    destination_path TEXT NOT NULL,
    final_path TEXT NOT NULL,
    temporary_path TEXT NOT NULL,
    backup_path TEXT NOT NULL,
    source_size INTEGER NOT NULL CHECK (source_size > 0),
    destination_size INTEGER NOT NULL CHECK (destination_size > 0),
    bytes_copied INTEGER NOT NULL DEFAULT 0 CHECK (bytes_copied >= 0 AND bytes_copied <= destination_size),
    verification_status TEXT NOT NULL CHECK (verification_status IN ('NotVerified', 'Verified', 'Warning', 'Failed')),
    failure_message TEXT NULL,
    date_created_utc TEXT NOT NULL,
    date_updated_utc TEXT NOT NULL,
    FOREIGN KEY (completed_encode_id) REFERENCES completed_encodes (id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_replacement_operations_completed_encode
    ON replacement_operations (completed_encode_id);

CREATE INDEX IF NOT EXISTS ix_replacement_operations_status
    ON replacement_operations (status, date_updated_utc DESC);

CREATE UNIQUE INDEX IF NOT EXISTS ux_replacement_operations_active_encode
    ON replacement_operations (completed_encode_id)
    WHERE status IN ('Planned', 'InProgress', 'Failed');
