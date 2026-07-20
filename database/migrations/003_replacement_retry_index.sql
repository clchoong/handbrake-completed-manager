PRAGMA foreign_keys = ON;

DROP INDEX IF EXISTS ux_replacement_operations_active_encode;

CREATE UNIQUE INDEX ux_replacement_operations_active_encode
    ON replacement_operations (completed_encode_id)
    WHERE status IN ('Planned', 'InProgress');
