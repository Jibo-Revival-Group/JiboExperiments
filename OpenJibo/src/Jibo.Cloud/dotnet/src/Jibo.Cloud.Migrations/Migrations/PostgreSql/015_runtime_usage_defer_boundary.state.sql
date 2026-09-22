-- Dormant runtime-usage defer/backoff boundary. This migration grants no
-- capability; activation requires a separately reviewed, narrow wrapper.

CREATE TABLE IF NOT EXISTS RuntimeUsageOutboxDeferrals
(
    OperationId UUID NOT NULL PRIMARY KEY,
    MessageId UUID NOT NULL REFERENCES RuntimeUsageOutboxMessages (MessageId),
    LeaseOwner TEXT NOT NULL CHECK (LeaseOwner ~ '^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,127}$'),
    NotBeforeUtc TIMESTAMPTZ NOT NULL,
    FailureCategory TEXT NOT NULL CHECK (FailureCategory ~ '^[a-z][a-z0-9-]{0,63}$'),
    DeferredUtc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_RuntimeUsageOutboxDeferrals_Message
    ON RuntimeUsageOutboxDeferrals (MessageId, DeferredUtc);

CREATE OR REPLACE FUNCTION RejectRuntimeUsageOutboxDeferralMutation()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'runtime usage defer receipt is immutable' USING ERRCODE = '55000';
END;
$$;

DROP TRIGGER IF EXISTS TR_RuntimeUsageOutboxDeferrals_Immutable ON RuntimeUsageOutboxDeferrals;
CREATE TRIGGER TR_RuntimeUsageOutboxDeferrals_Immutable
    BEFORE UPDATE OR DELETE ON RuntimeUsageOutboxDeferrals
    FOR EACH ROW EXECUTE FUNCTION RejectRuntimeUsageOutboxDeferralMutation();

-- A live leased-to-pending transition is valid only when this transaction has
-- already written its exact immutable defer receipt.
CREATE OR REPLACE FUNCTION GuardRuntimeUsageOutboxDeliveryMutation()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
    v_has_exact_deferral BOOLEAN := FALSE;
BEGIN
    IF OLD.DeliveryState = 'leased' AND NEW.DeliveryState = 'pending' AND
       OLD.LeaseExpiresUtc > clock_timestamp() THEN
        SELECT EXISTS
        (
            SELECT 1 FROM RuntimeUsageOutboxDeferrals AS deferral
            WHERE deferral.MessageId = OLD.MessageId
              AND deferral.LeaseOwner = OLD.LeaseOwner
              AND deferral.NotBeforeUtc = NEW.NotBeforeUtc
              AND deferral.FailureCategory = NEW.LastFailureCategory
              AND deferral.DeferredUtc = NEW.UpdatedUtc
        ) INTO v_has_exact_deferral;
    END IF;

    IF TG_OP = 'DELETE' OR
       OLD.MessageId IS DISTINCT FROM NEW.MessageId OR
       NEW.AttemptCount < OLD.AttemptCount OR
       OLD.DeliveryState IN ('acknowledged', 'quarantined') OR
       (OLD.DeliveryState = 'pending' AND NEW.DeliveryState NOT IN ('pending', 'leased', 'quarantined')) OR
       (OLD.DeliveryState = 'leased' AND NEW.DeliveryState NOT IN
            ('pending', 'leased', 'acknowledged', 'quarantined')) OR
       (OLD.DeliveryState = 'leased' AND NEW.DeliveryState = 'pending' AND
            OLD.LeaseExpiresUtc > clock_timestamp() AND NOT v_has_exact_deferral) THEN
        RAISE EXCEPTION 'runtime usage delivery state transition is invalid' USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION DeferRuntimeUsageOutbox(
    p_message_id UUID,
    p_lease_owner TEXT,
    p_operation_id UUID,
    p_not_before_utc TIMESTAMPTZ,
    p_failure_category TEXT)
RETURNS TABLE (DeferredMessageId UUID, DeferredUtc TIMESTAMPTZ, WasReplay BOOLEAN)
LANGUAGE plpgsql AS $$
DECLARE
    v_now TIMESTAMPTZ;
    v_delivery RuntimeUsageOutboxDelivery%ROWTYPE;
    v_receipt RuntimeUsageOutboxDeferrals%ROWTYPE;
    v_updated_count INTEGER;
BEGIN
    IF p_message_id IS NULL OR p_message_id = '00000000-0000-0000-0000-000000000000' OR
       p_operation_id IS NULL OR p_operation_id = '00000000-0000-0000-0000-000000000000' OR
       p_lease_owner IS NULL OR p_lease_owner !~ '^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,127}$' OR
       p_not_before_utc IS NULL OR p_failure_category IS NULL OR
       p_failure_category !~ '^[a-z][a-z0-9-]{0,63}$' THEN
        RAISE EXCEPTION 'runtime usage defer request is invalid' USING ERRCODE = '22023';
    END IF;

    -- Serialize operation UUID reuse even across different messages.
    PERFORM pg_advisory_xact_lock(hashtextextended(p_operation_id::TEXT, 0));
    SELECT * INTO v_receipt FROM RuntimeUsageOutboxDeferrals AS deferral
    WHERE deferral.OperationId = p_operation_id;
    IF FOUND THEN
        IF v_receipt.MessageId IS DISTINCT FROM p_message_id OR
           v_receipt.LeaseOwner IS DISTINCT FROM p_lease_owner OR
           v_receipt.NotBeforeUtc IS DISTINCT FROM p_not_before_utc OR
           v_receipt.FailureCategory IS DISTINCT FROM p_failure_category THEN
            RAISE EXCEPTION 'runtime usage defer conflicts with prior operation' USING ERRCODE = '22023';
        END IF;
        RETURN QUERY SELECT p_message_id, v_receipt.DeferredUtc, TRUE;
        RETURN;
    END IF;

    v_now := clock_timestamp();
    IF p_not_before_utc <= v_now OR p_not_before_utc > v_now + INTERVAL '24 hours' THEN
        RAISE EXCEPTION 'runtime usage defer backoff is outside the allowed window' USING ERRCODE = '22023';
    END IF;

    SELECT * INTO v_delivery FROM RuntimeUsageOutboxDelivery AS delivery
    WHERE delivery.MessageId = p_message_id FOR UPDATE;
    IF NOT FOUND OR v_delivery.DeliveryState <> 'leased' OR
       v_delivery.LeaseOwner IS DISTINCT FROM p_lease_owner OR
       v_delivery.LeaseExpiresUtc IS NULL OR v_delivery.LeaseExpiresUtc <= clock_timestamp() THEN
        RAISE EXCEPTION 'runtime usage defer lease is invalid' USING ERRCODE = '22023';
    END IF;

    INSERT INTO RuntimeUsageOutboxDeferrals
        (OperationId, MessageId, LeaseOwner, NotBeforeUtc, FailureCategory, DeferredUtc)
    VALUES (p_operation_id, p_message_id, p_lease_owner, p_not_before_utc, p_failure_category, v_now);

    UPDATE RuntimeUsageOutboxDelivery
    SET DeliveryState = 'pending', LeaseOwner = NULL, LeaseExpiresUtc = NULL,
        NotBeforeUtc = p_not_before_utc, LastFailureCategory = p_failure_category, UpdatedUtc = v_now
    WHERE MessageId = p_message_id AND DeliveryState = 'leased'
      AND LeaseOwner = p_lease_owner AND LeaseExpiresUtc > clock_timestamp();
    GET DIAGNOSTICS v_updated_count = ROW_COUNT;
    IF v_updated_count <> 1 THEN
        RAISE EXCEPTION 'runtime usage defer lease is invalid' USING ERRCODE = '22023';
    END IF;
    RETURN QUERY SELECT p_message_id, v_now, FALSE;
END;
$$;

-- Close the check-then-update lease-expiry window in terminal operations.
CREATE OR REPLACE FUNCTION AcknowledgeRuntimeUsageOutbox(
    p_message_id UUID, p_lease_owner TEXT, p_receipt_hash BYTEA)
RETURNS TABLE (AcknowledgedMessageId UUID, AcknowledgedUtc TIMESTAMPTZ, WasReplay BOOLEAN)
LANGUAGE plpgsql AS $$
DECLARE
    v_now TIMESTAMPTZ;
    v_delivery RuntimeUsageOutboxDelivery%ROWTYPE;
    v_updated_count INTEGER;
BEGIN
    IF p_message_id IS NULL OR p_message_id = '00000000-0000-0000-0000-000000000000' OR
       p_lease_owner IS NULL OR p_lease_owner !~ '^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,127}$' OR
       p_receipt_hash IS NULL OR octet_length(p_receipt_hash) <> 32 THEN
        RAISE EXCEPTION 'runtime usage acknowledgement is invalid' USING ERRCODE = '22023';
    END IF;
    SELECT * INTO v_delivery FROM RuntimeUsageOutboxDelivery AS delivery
    WHERE delivery.MessageId = p_message_id FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'runtime usage acknowledgement is invalid' USING ERRCODE = '22023'; END IF;
    IF v_delivery.DeliveryState = 'acknowledged' THEN
        IF v_delivery.ReceiptHash IS DISTINCT FROM p_receipt_hash THEN
            RAISE EXCEPTION 'runtime usage acknowledgement conflicts with prior receipt' USING ERRCODE = '22023';
        END IF;
        RETURN QUERY SELECT p_message_id, v_delivery.AcknowledgedUtc, TRUE; RETURN;
    END IF;
    v_now := clock_timestamp();
    IF v_delivery.DeliveryState <> 'leased' OR v_delivery.LeaseOwner IS DISTINCT FROM p_lease_owner OR
       v_delivery.LeaseExpiresUtc IS NULL OR v_delivery.LeaseExpiresUtc <= v_now THEN
        RAISE EXCEPTION 'runtime usage acknowledgement lease is invalid' USING ERRCODE = '22023';
    END IF;
    UPDATE RuntimeUsageOutboxDelivery
    SET DeliveryState = 'acknowledged', LeaseOwner = NULL, LeaseExpiresUtc = NULL,
        AcknowledgedUtc = v_now, ReceiptHash = p_receipt_hash, UpdatedUtc = v_now
    WHERE MessageId = p_message_id AND DeliveryState = 'leased'
      AND LeaseOwner = p_lease_owner AND LeaseExpiresUtc > clock_timestamp();
    GET DIAGNOSTICS v_updated_count = ROW_COUNT;
    IF v_updated_count <> 1 THEN
        RAISE EXCEPTION 'runtime usage acknowledgement lease is invalid' USING ERRCODE = '22023';
    END IF;
    RETURN QUERY SELECT p_message_id, v_now, FALSE;
END;
$$;

CREATE OR REPLACE FUNCTION QuarantineRuntimeUsageOutbox(
    p_message_id UUID, p_lease_owner TEXT, p_quarantine_category TEXT)
RETURNS TABLE (QuarantinedMessageId UUID, QuarantinedUtc TIMESTAMPTZ, WasReplay BOOLEAN)
LANGUAGE plpgsql AS $$
DECLARE
    v_now TIMESTAMPTZ;
    v_delivery RuntimeUsageOutboxDelivery%ROWTYPE;
    v_updated_count INTEGER;
BEGIN
    IF p_message_id IS NULL OR p_message_id = '00000000-0000-0000-0000-000000000000' OR
       p_lease_owner IS NULL OR p_lease_owner !~ '^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,127}$' OR
       p_quarantine_category IS NULL OR p_quarantine_category !~ '^[a-z][a-z0-9-]{0,63}$' THEN
        RAISE EXCEPTION 'runtime usage quarantine request is invalid' USING ERRCODE = '22023';
    END IF;
    SELECT * INTO v_delivery FROM RuntimeUsageOutboxDelivery AS delivery
    WHERE delivery.MessageId = p_message_id FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'runtime usage quarantine request is invalid' USING ERRCODE = '22023'; END IF;
    IF v_delivery.DeliveryState = 'quarantined' THEN
        IF v_delivery.QuarantineCategory IS DISTINCT FROM p_quarantine_category THEN
            RAISE EXCEPTION 'runtime usage quarantine conflicts with prior category' USING ERRCODE = '22023';
        END IF;
        RETURN QUERY SELECT p_message_id, v_delivery.QuarantinedUtc, TRUE; RETURN;
    END IF;
    v_now := clock_timestamp();
    IF v_delivery.DeliveryState <> 'leased' OR v_delivery.LeaseOwner IS DISTINCT FROM p_lease_owner OR
       v_delivery.LeaseExpiresUtc IS NULL OR v_delivery.LeaseExpiresUtc <= v_now THEN
        RAISE EXCEPTION 'runtime usage quarantine lease is invalid' USING ERRCODE = '22023';
    END IF;
    UPDATE RuntimeUsageOutboxDelivery
    SET DeliveryState = 'quarantined', LeaseOwner = NULL, LeaseExpiresUtc = NULL,
        QuarantinedUtc = v_now, QuarantineCategory = p_quarantine_category, UpdatedUtc = v_now
    WHERE MessageId = p_message_id AND DeliveryState = 'leased'
      AND LeaseOwner = p_lease_owner AND LeaseExpiresUtc > clock_timestamp();
    GET DIAGNOSTICS v_updated_count = ROW_COUNT;
    IF v_updated_count <> 1 THEN
        RAISE EXCEPTION 'runtime usage quarantine lease is invalid' USING ERRCODE = '22023';
    END IF;
    RETURN QUERY SELECT p_message_id, v_now, FALSE;
END;
$$;

REVOKE ALL ON RuntimeUsageOutboxDeferrals FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION RejectRuntimeUsageOutboxDeferralMutation() FROM PUBLIC;
REVOKE ALL ON FUNCTION DeferRuntimeUsageOutbox(UUID, TEXT, UUID, TIMESTAMPTZ, TEXT) FROM PUBLIC;
REVOKE ALL ON FUNCTION AcknowledgeRuntimeUsageOutbox(UUID, TEXT, BYTEA) FROM PUBLIC;
REVOKE ALL ON FUNCTION QuarantineRuntimeUsageOutbox(UUID, TEXT, TEXT) FROM PUBLIC;
