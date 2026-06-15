CREATE TABLE IF NOT EXISTS PersistenceSnapshots
(
    SnapshotName
    TEXT
    NOT
    NULL
    PRIMARY
    KEY,
    SnapshotJson
    TEXT
    NOT
    NULL,
    CreatedUtc
    TIMESTAMPTZ
    NOT
    NULL
    DEFAULT
    NOW
(
),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW
(
)
    );
