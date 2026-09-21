-- Execute replay observation through a narrowly grantable SECURITY DEFINER boundary.
-- All referenced objects are schema-qualified because the function deliberately uses
-- a restricted search_path. Deployment grants EXECUTE to a dedicated login later.

CREATE OR REPLACE FUNCTION public.ObserveAwsSigV4Replay(
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
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_now TIMESTAMPTZ := pg_catalog.clock_timestamp();
BEGIN
    IF p_replay_digest IS NULL OR pg_catalog.octet_length(p_replay_digest) <> 32 OR
       p_key_version IS NULL OR p_key_version NOT BETWEEN 1 AND 32767 OR
       p_operation IS NULL OR
       p_operation NOT IN ('Account.CreateHubToken', 'Notification.NewRobotToken') THEN
        RAISE EXCEPTION 'SigV4 replay observation is invalid'
            USING ERRCODE = '22023';
    END IF;

    WITH expired AS
    (
        SELECT candidate.ReplayDigest
        FROM public.AwsSigV4ReplayObservations AS candidate
        WHERE candidate.ExpiresUtc <= v_now
          AND candidate.ReplayDigest <> p_replay_digest
        ORDER BY candidate.ExpiresUtc
        LIMIT 256
    )
    DELETE FROM public.AwsSigV4ReplayObservations AS observation
    USING expired
    WHERE observation.ReplayDigest = expired.ReplayDigest;

    RETURN QUERY
    INSERT INTO public.AwsSigV4ReplayObservations AS observation
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

REVOKE ALL ON FUNCTION public.ObserveAwsSigV4Replay(BYTEA, SMALLINT, TEXT) FROM PUBLIC;
