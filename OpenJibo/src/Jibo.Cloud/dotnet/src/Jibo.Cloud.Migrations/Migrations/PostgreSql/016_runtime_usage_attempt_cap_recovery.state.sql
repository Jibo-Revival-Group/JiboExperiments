-- Dormant runtime-usage attempt-cap recovery boundary.
--
-- This is an explicit audited recovery operation, not a retry/reset path. A
-- message at the hard delivery cap is moved to a fixed terminal quarantine
-- category while its attempt count and source evidence remain unchanged.
-- No role is granted by this migration.

CREATE TABLE IF NOT EXISTS RuntimeUsageOutboxAttemptCapRecoveryReceipts
(
    RecoveryOperationId UUID NOT NULL CHECK (
        RecoveryOperationId <> '00000000-0000-0000-0000-000000000000'),
    MessageId UUID NOT NULL REFERENCES RuntimeUsageOutboxMessages(MessageId),
    EvidenceDigest BYTEA NOT NULL CHECK (OCTET_LENGTH(EvidenceDigest) = 32),
    PriorDeliveryState TEXT NOT NULL CHECK (PriorDeliveryState IN ('pending', 'leased')),
    PriorAttemptCount INTEGER NOT NULL CHECK (PriorAttemptCount = 100000),
    PriorNotBeforeUtc TIMESTAMPTZ NOT NULL,
    PriorLeaseExpiresUtc TIMESTAMPTZ NULL,
    SessionUser TEXT NOT NULL CHECK (OCTET_LENGTH(SessionUser) BETWEEN 1 AND 256),
    CurrentUser TEXT NOT NULL CHECK (OCTET_LENGTH(CurrentUser) BETWEEN 1 AND 256),
    RecoveredUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ActionCode TEXT NOT NULL CHECK (ActionCode = 'quarantine'),
    QuarantineCategory TEXT NOT NULL CHECK (QuarantineCategory = 'attempt-cap-exhausted'),
    PRIMARY KEY (RecoveryOperationId),
    CHECK ((PriorDeliveryState = 'pending' AND PriorLeaseExpiresUtc IS NULL) OR
           (PriorDeliveryState = 'leased' AND PriorLeaseExpiresUtc IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS IX_RuntimeUsageOutboxAttemptCapRecoveryReceipts_Message
    ON RuntimeUsageOutboxAttemptCapRecoveryReceipts(MessageId, RecoveredUtc DESC);

CREATE OR REPLACE FUNCTION RejectRuntimeUsageOutboxAttemptCapRecoveryMutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'runtime usage attempt-cap recovery receipts are immutable'
        USING ERRCODE = '55000';
END;
$$;

DROP TRIGGER IF EXISTS TR_RuntimeUsageOutboxAttemptCapRecovery_Immutable
    ON RuntimeUsageOutboxAttemptCapRecoveryReceipts;
CREATE TRIGGER TR_RuntimeUsageOutboxAttemptCapRecovery_Immutable
    BEFORE UPDATE OR DELETE ON RuntimeUsageOutboxAttemptCapRecoveryReceipts
    FOR EACH ROW EXECUTE FUNCTION RejectRuntimeUsageOutboxAttemptCapRecoveryMutation();

CREATE OR REPLACE FUNCTION RecoverRuntimeUsageOutboxAtAttemptCap(
    p_message_id UUID,
    p_recovery_operation_id UUID,
    p_evidence_digest BYTEA)
RETURNS TABLE
(
    RecoveredMessageId UUID,
    RecoveredUtc TIMESTAMPTZ,
    WasReplay BOOLEAN
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_now TIMESTAMPTZ;
    v_receipt RuntimeUsageOutboxAttemptCapRecoveryReceipts%ROWTYPE;
    v_delivery RuntimeUsageOutboxDelivery%ROWTYPE;
    v_updated_count INTEGER;
BEGIN
    IF p_message_id IS NULL OR
       p_message_id = '00000000-0000-0000-0000-000000000000' OR
       p_recovery_operation_id IS NULL OR
       p_recovery_operation_id = '00000000-0000-0000-0000-000000000000' OR
       p_evidence_digest IS NULL OR OCTET_LENGTH(p_evidence_digest) <> 32 THEN
        RAISE EXCEPTION 'runtime usage attempt-cap recovery request is invalid'
            USING ERRCODE = '22023';
    END IF;

    -- Serialize operation retries, including reuse of one operation for a
    -- different message. A lost response can therefore be replayed safely.
    PERFORM pg_advisory_xact_lock(hashtextextended(p_recovery_operation_id::TEXT, 0));

    SELECT * INTO v_receipt
    FROM RuntimeUsageOutboxAttemptCapRecoveryReceipts AS receipt
    WHERE receipt.RecoveryOperationId = p_recovery_operation_id;
    IF FOUND THEN
        IF v_receipt.MessageId IS DISTINCT FROM p_message_id OR
           v_receipt.EvidenceDigest IS DISTINCT FROM p_evidence_digest THEN
            RAISE EXCEPTION 'runtime usage attempt-cap recovery operation conflicts with prior receipt'
                USING ERRCODE = '22023';
        END IF;
        RETURN QUERY SELECT p_message_id, v_receipt.RecoveredUtc, TRUE;
        RETURN;
    END IF;

    SELECT * INTO v_delivery
    FROM RuntimeUsageOutboxDelivery AS delivery
    WHERE delivery.MessageId = p_message_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'runtime usage attempt-cap recovery message is invalid'
            USING ERRCODE = '22023';
    END IF;

    -- Take the time boundary only after both locks. Eligibility and the
    -- guarded update therefore use one current, serialized observation.
    v_now := clock_timestamp();

    IF v_delivery.AttemptCount <> 100000 THEN
        RAISE EXCEPTION 'runtime usage attempt-cap recovery requires exactly 100000 attempts'
            USING ERRCODE = '22023';
    END IF;

    IF v_delivery.DeliveryState = 'pending' AND v_delivery.NotBeforeUtc > v_now THEN
        RAISE EXCEPTION 'runtime usage attempt-cap recovery requires a ready pending message'
            USING ERRCODE = '22023';
    END IF;

    IF v_delivery.DeliveryState = 'leased' AND
       (v_delivery.LeaseExpiresUtc IS NULL OR v_delivery.LeaseExpiresUtc > v_now) THEN
        RAISE EXCEPTION 'runtime usage attempt-cap recovery rejects a live lease'
            USING ERRCODE = '22023';
    END IF;

    IF v_delivery.DeliveryState NOT IN ('pending', 'leased') THEN
        RAISE EXCEPTION 'runtime usage attempt-cap recovery rejects a terminal message'
            USING ERRCODE = '22023';
    END IF;

    INSERT INTO RuntimeUsageOutboxAttemptCapRecoveryReceipts
        (RecoveryOperationId, MessageId, EvidenceDigest, PriorDeliveryState,
         PriorAttemptCount, PriorNotBeforeUtc, PriorLeaseExpiresUtc,
         SessionUser, CurrentUser, RecoveredUtc, ActionCode, QuarantineCategory)
    VALUES
        (p_recovery_operation_id, p_message_id, p_evidence_digest,
         v_delivery.DeliveryState, v_delivery.AttemptCount, v_delivery.NotBeforeUtc,
         v_delivery.LeaseExpiresUtc, session_user, current_user, v_now,
         'quarantine', 'attempt-cap-exhausted');

    UPDATE RuntimeUsageOutboxDelivery
    SET DeliveryState = 'quarantined',
        LeaseOwner = NULL,
        LeaseExpiresUtc = NULL,
        QuarantinedUtc = v_now,
        QuarantineCategory = 'attempt-cap-exhausted',
        UpdatedUtc = v_now
    WHERE MessageId = p_message_id
      AND AttemptCount = 100000
      AND ((DeliveryState = 'pending' AND NotBeforeUtc <= v_now) OR
           (DeliveryState = 'leased' AND LeaseExpiresUtc IS NOT NULL AND
            LeaseExpiresUtc <= v_now));
    GET DIAGNOSTICS v_updated_count = ROW_COUNT;
    IF v_updated_count <> 1 THEN
        RAISE EXCEPTION 'runtime usage attempt-cap recovery eligibility changed'
            USING ERRCODE = '22023';
    END IF;

    RETURN QUERY SELECT p_message_id, v_now, FALSE;
END;
$$;

REVOKE ALL ON RuntimeUsageOutboxAttemptCapRecoveryReceipts FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION RejectRuntimeUsageOutboxAttemptCapRecoveryMutation() FROM PUBLIC;
REVOKE ALL ON FUNCTION RecoverRuntimeUsageOutboxAtAttemptCap(UUID, UUID, BYTEA) FROM PUBLIC;
