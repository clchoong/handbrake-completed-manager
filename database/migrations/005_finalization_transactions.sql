PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS finalization_transactions (
    operation_id TEXT PRIMARY KEY NOT NULL,
    checkpoint TEXT NOT NULL CHECK (checkpoint IN (
        'Prepared',
        'PromoteTemporaryIntentRecorded',
        'FinalPromoted',
        'RecycleSourceIntentRecorded',
        'SourceRecycled',
        'Completed',
        'RecoveryRequired',
        'UndoPrepared',
        'RestoreSourceIntentRecorded',
        'SourceRestored',
        'RecycleFinalIntentRecorded',
        'FinalRecycled',
        'Undone')),
    source_sha256 TEXT NOT NULL CHECK (length(source_sha256) = 64),
    final_sha256 TEXT NOT NULL CHECK (length(final_sha256) = 64),
    revision INTEGER NOT NULL DEFAULT 0 CHECK (revision >= 0),
    failure_message TEXT NULL,
    date_created_utc TEXT NOT NULL,
    date_updated_utc TEXT NOT NULL,
    FOREIGN KEY (operation_id) REFERENCES replacement_operations (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_finalization_transactions_checkpoint
    ON finalization_transactions (checkpoint, date_updated_utc DESC);
