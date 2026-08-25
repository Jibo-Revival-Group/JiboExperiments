CREATE TABLE IF NOT EXISTS CloudStateImportRejections
(
    RejectionId BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    SourceSnapshotName TEXT NOT NULL,
    SourceSha256 CHAR(64) NOT NULL,
    EntityType TEXT NOT NULL,
    EntityKey TEXT NOT NULL,
    Reason TEXT NOT NULL,
    Payload JSONB NOT NULL,
    RejectedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (SourceSha256 ~ '^[0-9a-fA-F]{64}$'),
    UNIQUE (SourceSnapshotName, SourceSha256, EntityType, EntityKey, Reason)
);

CREATE INDEX IF NOT EXISTS IX_CloudStateImportRejections_Source
    ON CloudStateImportRejections (SourceSnapshotName, SourceSha256, EntityType);
