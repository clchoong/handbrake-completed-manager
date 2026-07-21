CREATE TABLE IF NOT EXISTS completed_encode_actions (
    completed_encode_id TEXT PRIMARY KEY NOT NULL,
    replacement_path TEXT NULL,
    action_status TEXT NOT NULL,
    output_kept INTEGER NULL,
    date_updated_utc TEXT NOT NULL,
    FOREIGN KEY (completed_encode_id) REFERENCES completed_encodes(id) ON DELETE CASCADE,
    CHECK (action_status IN (
        'Output Deleted',
        'Source Replaced',
        'Source Replaced, Output Kept')),
    CHECK (output_kept IS NULL OR output_kept IN (0, 1))
);
