-- Observe-only, cross-replica replay classification for verified legacy SigV4 traffic.
--
-- ReplayDigest is an environment-keyed HMAC produced by the application. Raw access
-- keys, signatures, request headers, targets, bodies, and device identifiers
-- must never be persisted here. No result from this table authorizes or rejects traffic.

CREATE TABLE IF NOT EXISTS AwsSigV4ReplayObservations
(
    ReplayDigest BYTEA PRIMARY KEY,
    KeyVersion SMALLINT NOT NULL,
    Operation TEXT NOT NULL,
    FirstSeenUtc TIMESTAMPTZ NOT NULL,
    LastSeenUtc TIMESTAMPTZ NOT NULL,
    ExpiresUtc TIMESTAMPTZ NOT NULL,
    ObservationCount BIGINT NOT NULL,
    CONSTRAINT CK_AwsSigV4ReplayObservations_Digest
        CHECK (OCTET_LENGTH(ReplayDigest) = 32),
    CONSTRAINT CK_AwsSigV4ReplayObservations_KeyVersion
        CHECK (KeyVersion BETWEEN 1 AND 32767),
    CONSTRAINT CK_AwsSigV4ReplayObservations_Operation
        CHECK (Operation IN ('Account.CreateHubToken', 'Notification.NewRobotToken')),
    CONSTRAINT CK_AwsSigV4ReplayObservations_Timestamps
        CHECK (FirstSeenUtc <= LastSeenUtc AND LastSeenUtc < ExpiresUtc),
    CONSTRAINT CK_AwsSigV4ReplayObservations_Count
        CHECK (ObservationCount >= 1)
);

CREATE INDEX IF NOT EXISTS IX_AwsSigV4ReplayObservations_ExpiresUtc
    ON AwsSigV4ReplayObservations (ExpiresUtc);

CREATE OR REPLACE FUNCTION ObserveAwsSigV4Replay(
    p_replay_digest BYTEA,
    p_key_version SMALLINT,
    p_operation TEXT)
RETURNS TABLE
(
    WasReplay BOOLEAN,
    ObservationCount BIGINT,
    FirstSeenUtc TIMESTAMPTZ,
    LastSeenUtc TIMESTAMPTZ,
    ExpiresUtc TIMESTAMPTZ
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_now TIMESTAMPTZ := clock_timestamp();
BEGIN
    IF p_replay_digest IS NULL OR OCTET_LENGTH(p_replay_digest) <> 32 OR
       p_key_version IS NULL OR p_key_version NOT BETWEEN 1 AND 32767 OR
       p_operation IS NULL OR
       p_operation NOT IN ('Account.CreateHubToken', 'Notification.NewRobotToken') THEN
        RAISE EXCEPTION 'SigV4 replay observation is invalid'
            USING ERRCODE = '22023';
    END IF;

    -- Bound opportunistic cleanup so request latency cannot grow with table history.
    WITH expired AS
    (
        SELECT candidate.ReplayDigest
        FROM AwsSigV4ReplayObservations AS candidate
        WHERE candidate.ExpiresUtc <= v_now
          AND candidate.ReplayDigest <> p_replay_digest
        ORDER BY candidate.ExpiresUtc
        LIMIT 256
    )
    DELETE FROM AwsSigV4ReplayObservations AS observation
    USING expired
    WHERE observation.ReplayDigest = expired.ReplayDigest;

    RETURN QUERY
    INSERT INTO AwsSigV4ReplayObservations AS observation
        (ReplayDigest, KeyVersion, Operation, FirstSeenUtc, LastSeenUtc, ExpiresUtc, ObservationCount)
    VALUES
        (p_replay_digest, p_key_version, p_operation, v_now, v_now, v_now + INTERVAL '15 minutes', 1)
    ON CONFLICT (ReplayDigest) DO UPDATE
    SET KeyVersion = CASE
            WHEN observation.ExpiresUtc <= v_now THEN EXCLUDED.KeyVersion
            ELSE observation.KeyVersion
        END,
        Operation = CASE
            WHEN observation.ExpiresUtc <= v_now THEN EXCLUDED.Operation
            ELSE observation.Operation
        END,
        FirstSeenUtc = CASE
            WHEN observation.ExpiresUtc <= v_now THEN v_now
            ELSE observation.FirstSeenUtc
        END,
        LastSeenUtc = CASE
            WHEN observation.ExpiresUtc <= v_now THEN v_now
            ELSE GREATEST(observation.LastSeenUtc, v_now)
        END,
        ExpiresUtc = CASE
            WHEN observation.ExpiresUtc <= v_now THEN v_now + INTERVAL '15 minutes'
            ELSE observation.ExpiresUtc
        END,
        ObservationCount = CASE
            WHEN observation.ExpiresUtc <= v_now THEN 1
            ELSE observation.ObservationCount + 1
        END
    RETURNING observation.ObservationCount > 1,
              observation.ObservationCount,
              observation.FirstSeenUtc,
              observation.LastSeenUtc,
              observation.ExpiresUtc;
END;
$$;

COMMENT ON TABLE AwsSigV4ReplayObservations IS
    'Short-lived observe-only keyed digests for cross-replica SigV4 replay classification.';
COMMENT ON COLUMN AwsSigV4ReplayObservations.ReplayDigest IS
    'Environment-keyed HMAC only; never a raw signature, credential, body, target, or device identifier.';

REVOKE ALL ON FUNCTION ObserveAwsSigV4Replay(BYTEA, SMALLINT, TEXT) FROM PUBLIC;
-- Deliberately no GRANT: deployment privilege wiring is a separately reviewed milestone.
