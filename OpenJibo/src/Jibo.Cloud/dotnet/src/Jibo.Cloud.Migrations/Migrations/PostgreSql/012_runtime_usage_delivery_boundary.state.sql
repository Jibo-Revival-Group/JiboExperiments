-- Dormant runtime-usage delivery boundary.
--
-- This migration deliberately does not change migration 011 or grant any
-- application/collector capability.  The functions are owner-only until a
-- separately reviewed deployment artifact adds a narrowly scoped wrapper.

CREATE OR REPLACE FUNCTION ClaimRuntimeUsageOutbox(
    p_lease_owner TEXT,
    p_lease_seconds INTEGER DEFAULT 300,
    p_batch_size INTEGER DEFAULT 1)
RETURNS TABLE
(
    MessageId UUID,
    ManagedRobotId UUID,
    UsageDate DATE,
    ServiceEnvironment TEXT,
    SourceSequence BIGINT,
    AccumulatorRevision BIGINT,
    FormatVersion SMALLINT,
    SourceSchemaVersion TEXT,
    SourceRevision TEXT,
    SuccessfulTurns BIGINT,
    FailedTurns BIGINT,
    HttpRequests BIGINT,
    HttpRequestBytes BIGINT,
    HttpResponseBytes BIGINT,
    WebSocketInboundMessages BIGINT,
    WebSocketOutboundMessages BIGINT,
    WebSocketInboundBytes BIGINT,
    WebSocketOutboundBytes BIGINT,
    AudioInputBytes BIGINT,
    FirstEventUtc TIMESTAMPTZ,
    LastEventUtc TIMESTAMPTZ,
    IsIncomplete BOOLEAN,
    FirstIncompleteUtc TIMESTAMPTZ,
    IncompleteReasonCode TEXT,
    IdempotencyKey TEXT,
    DeliveryAttemptCount INTEGER,
    LeaseExpiresUtc TIMESTAMPTZ
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_now TIMESTAMPTZ := clock_timestamp();
BEGIN
    IF p_lease_owner IS NULL OR
       p_lease_owner !~ '^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,127}$' OR
       p_lease_seconds IS NULL OR p_lease_seconds NOT BETWEEN 1 AND 3600 OR
       p_batch_size IS NULL OR p_batch_size NOT BETWEEN 1 AND 100 THEN
        RAISE EXCEPTION 'runtime usage lease request is invalid'
            USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    WITH candidates AS
    (
        SELECT delivery.MessageId
        FROM RuntimeUsageOutboxDelivery AS delivery
        JOIN RuntimeUsageOutboxMessages AS message
          ON message.MessageId = delivery.MessageId
        WHERE delivery.NotBeforeUtc <= v_now
          AND delivery.AttemptCount < 100000
          AND (
              delivery.DeliveryState = 'pending' OR
              (delivery.DeliveryState = 'leased' AND
               delivery.LeaseExpiresUtc <= v_now)
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM RuntimeUsageOutboxMessages AS earlier_message
              JOIN RuntimeUsageOutboxDelivery AS earlier_delivery
                ON earlier_delivery.MessageId = earlier_message.MessageId
              WHERE earlier_message.ManagedRobotId = message.ManagedRobotId
                AND earlier_message.UsageDate = message.UsageDate
                AND earlier_message.ServiceEnvironment = message.ServiceEnvironment
                AND earlier_message.SourceSequence < message.SourceSequence
                AND earlier_delivery.DeliveryState <> 'acknowledged'
          )
        ORDER BY message.CreatedUtc, message.MessageId
        FOR UPDATE OF delivery SKIP LOCKED
        LIMIT p_batch_size
    )
    UPDATE RuntimeUsageOutboxDelivery AS delivery
    SET DeliveryState = 'leased',
        AttemptCount = delivery.AttemptCount + 1,
        LeaseOwner = p_lease_owner,
        LeaseExpiresUtc = v_now + make_interval(secs => p_lease_seconds),
        UpdatedUtc = v_now
    FROM candidates AS candidate
    JOIN RuntimeUsageOutboxMessages AS message
      ON message.MessageId = candidate.MessageId
    WHERE delivery.MessageId = candidate.MessageId
    RETURNING message.MessageId,
              message.ManagedRobotId,
              message.UsageDate,
              message.ServiceEnvironment,
              message.SourceSequence,
              message.AccumulatorRevision,
              message.FormatVersion,
              message.SourceSchemaVersion,
              message.SourceRevision,
              message.SuccessfulTurns,
              message.FailedTurns,
              message.HttpRequests,
              message.HttpRequestBytes,
              message.HttpResponseBytes,
              message.WebSocketInboundMessages,
              message.WebSocketOutboundMessages,
              message.WebSocketInboundBytes,
              message.WebSocketOutboundBytes,
              message.AudioInputBytes,
              message.FirstEventUtc,
              message.LastEventUtc,
              message.IsIncomplete,
              message.FirstIncompleteUtc,
              message.IncompleteReasonCode,
              message.IdempotencyKey,
              delivery.AttemptCount,
              delivery.LeaseExpiresUtc;
END;
$$;

CREATE OR REPLACE FUNCTION AcknowledgeRuntimeUsageOutbox(
    p_message_id UUID,
    p_lease_owner TEXT,
    p_receipt_hash BYTEA)
RETURNS TABLE
(
    AcknowledgedMessageId UUID,
    AcknowledgedUtc TIMESTAMPTZ,
    WasReplay BOOLEAN
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_now TIMESTAMPTZ := clock_timestamp();
    v_delivery RuntimeUsageOutboxDelivery%ROWTYPE;
BEGIN
    IF p_message_id IS NULL OR
       p_message_id = '00000000-0000-0000-0000-000000000000' OR
       p_lease_owner IS NULL OR
       p_lease_owner !~ '^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,127}$' OR
       p_receipt_hash IS NULL OR octet_length(p_receipt_hash) <> 32 THEN
        RAISE EXCEPTION 'runtime usage acknowledgement is invalid'
            USING ERRCODE = '22023';
    END IF;

    SELECT * INTO v_delivery
    FROM RuntimeUsageOutboxDelivery AS delivery
    WHERE delivery.MessageId = p_message_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'runtime usage acknowledgement is invalid'
            USING ERRCODE = '22023';
    END IF;

    IF v_delivery.DeliveryState = 'acknowledged' THEN
        IF v_delivery.ReceiptHash IS DISTINCT FROM p_receipt_hash THEN
            RAISE EXCEPTION 'runtime usage acknowledgement conflicts with prior receipt'
                USING ERRCODE = '22023';
        END IF;
        RETURN QUERY SELECT p_message_id, v_delivery.AcknowledgedUtc, TRUE;
        RETURN;
    END IF;

    IF v_delivery.DeliveryState <> 'leased' OR
       v_delivery.LeaseOwner IS DISTINCT FROM p_lease_owner OR
       v_delivery.LeaseExpiresUtc IS NULL OR v_delivery.LeaseExpiresUtc <= v_now THEN
        RAISE EXCEPTION 'runtime usage acknowledgement lease is invalid'
            USING ERRCODE = '22023';
    END IF;

    UPDATE RuntimeUsageOutboxDelivery
    SET DeliveryState = 'acknowledged',
        LeaseOwner = NULL,
        LeaseExpiresUtc = NULL,
        AcknowledgedUtc = v_now,
        ReceiptHash = p_receipt_hash,
        UpdatedUtc = v_now
    WHERE MessageId = p_message_id;

    RETURN QUERY SELECT p_message_id, v_now, FALSE;
END;
$$;

CREATE OR REPLACE FUNCTION QuarantineRuntimeUsageOutbox(
    p_message_id UUID,
    p_lease_owner TEXT,
    p_quarantine_category TEXT)
RETURNS TABLE
(
    QuarantinedMessageId UUID,
    QuarantinedUtc TIMESTAMPTZ,
    WasReplay BOOLEAN
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_now TIMESTAMPTZ := clock_timestamp();
    v_delivery RuntimeUsageOutboxDelivery%ROWTYPE;
BEGIN
    IF p_message_id IS NULL OR
       p_message_id = '00000000-0000-0000-0000-000000000000' OR
       p_lease_owner IS NULL OR
       p_lease_owner !~ '^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,127}$' OR
       p_quarantine_category IS NULL OR
       p_quarantine_category !~ '^[a-z][a-z0-9-]{0,63}$' THEN
        RAISE EXCEPTION 'runtime usage quarantine request is invalid'
            USING ERRCODE = '22023';
    END IF;

    SELECT * INTO v_delivery
    FROM RuntimeUsageOutboxDelivery AS delivery
    WHERE delivery.MessageId = p_message_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'runtime usage quarantine request is invalid'
            USING ERRCODE = '22023';
    END IF;

    IF v_delivery.DeliveryState = 'quarantined' THEN
        IF v_delivery.QuarantineCategory IS DISTINCT FROM p_quarantine_category THEN
            RAISE EXCEPTION 'runtime usage quarantine conflicts with prior category'
                USING ERRCODE = '22023';
        END IF;
        RETURN QUERY SELECT p_message_id, v_delivery.QuarantinedUtc, TRUE;
        RETURN;
    END IF;

    IF v_delivery.DeliveryState <> 'leased' OR
       v_delivery.LeaseOwner IS DISTINCT FROM p_lease_owner OR
       v_delivery.LeaseExpiresUtc IS NULL OR v_delivery.LeaseExpiresUtc <= v_now THEN
        RAISE EXCEPTION 'runtime usage quarantine lease is invalid'
            USING ERRCODE = '22023';
    END IF;

    UPDATE RuntimeUsageOutboxDelivery
    SET DeliveryState = 'quarantined',
        LeaseOwner = NULL,
        LeaseExpiresUtc = NULL,
        QuarantinedUtc = v_now,
        QuarantineCategory = p_quarantine_category,
        UpdatedUtc = v_now
    WHERE MessageId = p_message_id;

    RETURN QUERY SELECT p_message_id, v_now, FALSE;
END;
$$;

-- Keep this boundary dormant. The migration owner can validate it, but no
-- runtime, collector, or public role can execute it until a separate grant
-- artifact is reviewed and applied.
REVOKE ALL ON FUNCTION ClaimRuntimeUsageOutbox(TEXT, INTEGER, INTEGER) FROM PUBLIC;
REVOKE ALL ON FUNCTION AcknowledgeRuntimeUsageOutbox(UUID, TEXT, BYTEA) FROM PUBLIC;
REVOKE ALL ON FUNCTION QuarantineRuntimeUsageOutbox(UUID, TEXT, TEXT) FROM PUBLIC;
