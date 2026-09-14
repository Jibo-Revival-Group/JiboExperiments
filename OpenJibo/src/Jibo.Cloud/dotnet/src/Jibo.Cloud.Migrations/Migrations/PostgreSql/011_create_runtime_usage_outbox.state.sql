CREATE TABLE IF NOT EXISTS RuntimeUsageRobotBindings
(
    SourceSubjectHmac BYTEA NOT NULL CHECK (OCTET_LENGTH(SourceSubjectHmac) = 32),
    BindingVersion INTEGER NOT NULL CHECK (BindingVersion > 0),
    ManagedRobotId UUID NOT NULL,
    ActiveFromUtc TIMESTAMPTZ NOT NULL,
    RevokedUtc TIMESTAMPTZ NULL,
    ActorCode TEXT NOT NULL CHECK (ActorCode ~ '^[a-z][a-z0-9-]{0,63}$'),
    ReasonCode TEXT NOT NULL CHECK (ReasonCode ~ '^[a-z][a-z0-9-]{0,63}$'),
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (SourceSubjectHmac, BindingVersion),
    CHECK (RevokedUtc IS NULL OR RevokedUtc >= ActiveFromUtc)
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_RuntimeUsageRobotBindings_ActiveSubject
    ON RuntimeUsageRobotBindings (SourceSubjectHmac)
    WHERE RevokedUtc IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS UX_RuntimeUsageRobotBindings_ActiveRobot
    ON RuntimeUsageRobotBindings (ManagedRobotId)
    WHERE RevokedUtc IS NULL;

CREATE OR REPLACE FUNCTION GuardRuntimeUsageRobotBindingMutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' OR
       OLD.SourceSubjectHmac IS DISTINCT FROM NEW.SourceSubjectHmac OR
       OLD.BindingVersion IS DISTINCT FROM NEW.BindingVersion OR
       OLD.ManagedRobotId IS DISTINCT FROM NEW.ManagedRobotId OR
       OLD.ActiveFromUtc IS DISTINCT FROM NEW.ActiveFromUtc OR
       OLD.ActorCode IS DISTINCT FROM NEW.ActorCode OR
       OLD.ReasonCode IS DISTINCT FROM NEW.ReasonCode OR
       OLD.CreatedUtc IS DISTINCT FROM NEW.CreatedUtc OR
       OLD.RevokedUtc IS NOT NULL OR NEW.RevokedUtc IS NULL THEN
        RAISE EXCEPTION 'runtime usage robot bindings are append-only and may only be revoked once'
            USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS TR_RuntimeUsageRobotBindings_Guard
    ON RuntimeUsageRobotBindings;
CREATE TRIGGER TR_RuntimeUsageRobotBindings_Guard
    BEFORE UPDATE OR DELETE ON RuntimeUsageRobotBindings
    FOR EACH ROW EXECUTE FUNCTION GuardRuntimeUsageRobotBindingMutation();

CREATE TABLE IF NOT EXISTS RuntimeUsageDailyAccumulators
(
    ManagedRobotId UUID NOT NULL,
    UsageDate DATE NOT NULL,
    ServiceEnvironment TEXT NOT NULL
        CHECK (ServiceEnvironment ~ '^[a-z][a-z0-9-]{0,31}$'),
    SuccessfulTurns BIGINT NOT NULL DEFAULT 0 CHECK (SuccessfulTurns >= 0),
    FailedTurns BIGINT NOT NULL DEFAULT 0 CHECK (FailedTurns >= 0),
    HttpRequests BIGINT NOT NULL DEFAULT 0 CHECK (HttpRequests >= 0),
    HttpRequestBytes BIGINT NOT NULL DEFAULT 0 CHECK (HttpRequestBytes >= 0),
    HttpResponseBytes BIGINT NOT NULL DEFAULT 0 CHECK (HttpResponseBytes >= 0),
    WebSocketInboundMessages BIGINT NOT NULL DEFAULT 0 CHECK (WebSocketInboundMessages >= 0),
    WebSocketOutboundMessages BIGINT NOT NULL DEFAULT 0 CHECK (WebSocketOutboundMessages >= 0),
    WebSocketInboundBytes BIGINT NOT NULL DEFAULT 0 CHECK (WebSocketInboundBytes >= 0),
    WebSocketOutboundBytes BIGINT NOT NULL DEFAULT 0 CHECK (WebSocketOutboundBytes >= 0),
    AudioInputBytes BIGINT NOT NULL DEFAULT 0 CHECK (AudioInputBytes >= 0),
    FirstEventUtc TIMESTAMPTZ NOT NULL,
    LastEventUtc TIMESTAMPTZ NOT NULL,
    Revision BIGINT NOT NULL CHECK (Revision > 0),
    NextSourceSequence BIGINT NOT NULL DEFAULT 1 CHECK (NextSourceSequence > 0),
    LastScheduledRevision BIGINT NOT NULL DEFAULT 0
        CHECK (LastScheduledRevision >= 0 AND LastScheduledRevision <= Revision),
    IsIncomplete BOOLEAN NOT NULL DEFAULT FALSE,
    FirstIncompleteUtc TIMESTAMPTZ NULL,
    IncompleteReasonCode TEXT NULL,
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (ManagedRobotId, UsageDate, ServiceEnvironment),
    CHECK (FirstEventUtc <= LastEventUtc),
    CHECK ((FirstEventUtc AT TIME ZONE 'UTC')::DATE = UsageDate),
    CHECK ((LastEventUtc AT TIME ZONE 'UTC')::DATE = UsageDate),
    CHECK (
        IsIncomplete OR SuccessfulTurns > 0 OR FailedTurns > 0 OR HttpRequests > 0 OR
        HttpRequestBytes > 0 OR HttpResponseBytes > 0 OR
        WebSocketInboundMessages > 0 OR WebSocketOutboundMessages > 0 OR
        WebSocketInboundBytes > 0 OR WebSocketOutboundBytes > 0 OR AudioInputBytes > 0
    ),
    CHECK (
        (IsIncomplete AND FirstIncompleteUtc IS NOT NULL AND
            IncompleteReasonCode ~ '^[a-z][a-z0-9-]{0,63}$') OR
        (NOT IsIncomplete AND FirstIncompleteUtc IS NULL AND IncompleteReasonCode IS NULL)
    )
);

CREATE TABLE IF NOT EXISTS RuntimeUsageAppliedEvents
(
    UsageEventId UUID NOT NULL PRIMARY KEY,
    ManagedRobotId UUID NOT NULL,
    UsageDate DATE NOT NULL,
    ServiceEnvironment TEXT NOT NULL,
    OccurredUtc TIMESTAMPTZ NOT NULL,
    SuccessfulTurnsDelta BIGINT NOT NULL CHECK (SuccessfulTurnsDelta >= 0),
    FailedTurnsDelta BIGINT NOT NULL CHECK (FailedTurnsDelta >= 0),
    HttpRequestsDelta BIGINT NOT NULL CHECK (HttpRequestsDelta >= 0),
    HttpRequestBytesDelta BIGINT NOT NULL CHECK (HttpRequestBytesDelta >= 0),
    HttpResponseBytesDelta BIGINT NOT NULL CHECK (HttpResponseBytesDelta >= 0),
    WebSocketInboundMessagesDelta BIGINT NOT NULL CHECK (WebSocketInboundMessagesDelta >= 0),
    WebSocketOutboundMessagesDelta BIGINT NOT NULL CHECK (WebSocketOutboundMessagesDelta >= 0),
    WebSocketInboundBytesDelta BIGINT NOT NULL CHECK (WebSocketInboundBytesDelta >= 0),
    WebSocketOutboundBytesDelta BIGINT NOT NULL CHECK (WebSocketOutboundBytesDelta >= 0),
    AudioInputBytesDelta BIGINT NOT NULL CHECK (AudioInputBytesDelta >= 0),
    IncompleteReasonCode TEXT NULL CHECK (
        IncompleteReasonCode IS NULL OR IncompleteReasonCode ~ '^[a-z][a-z0-9-]{0,63}$'),
    AppliedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    FOREIGN KEY (ManagedRobotId, UsageDate, ServiceEnvironment)
        REFERENCES RuntimeUsageDailyAccumulators
            (ManagedRobotId, UsageDate, ServiceEnvironment)
        DEFERRABLE INITIALLY DEFERRED,
    CHECK ((OccurredUtc AT TIME ZONE 'UTC')::DATE = UsageDate),
    CHECK (
        IncompleteReasonCode IS NOT NULL OR SuccessfulTurnsDelta > 0 OR FailedTurnsDelta > 0 OR
        HttpRequestsDelta > 0 OR HttpRequestBytesDelta > 0 OR HttpResponseBytesDelta > 0 OR
        WebSocketInboundMessagesDelta > 0 OR WebSocketOutboundMessagesDelta > 0 OR
        WebSocketInboundBytesDelta > 0 OR WebSocketOutboundBytesDelta > 0 OR
        AudioInputBytesDelta > 0
    )
);

CREATE OR REPLACE FUNCTION GuardRuntimeUsageAccumulatorMutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' OR
       OLD.ManagedRobotId IS DISTINCT FROM NEW.ManagedRobotId OR
       OLD.UsageDate IS DISTINCT FROM NEW.UsageDate OR
       OLD.ServiceEnvironment IS DISTINCT FROM NEW.ServiceEnvironment OR
       NEW.SuccessfulTurns < OLD.SuccessfulTurns OR
       NEW.FailedTurns < OLD.FailedTurns OR
       NEW.HttpRequests < OLD.HttpRequests OR
       NEW.HttpRequestBytes < OLD.HttpRequestBytes OR
       NEW.HttpResponseBytes < OLD.HttpResponseBytes OR
       NEW.WebSocketInboundMessages < OLD.WebSocketInboundMessages OR
       NEW.WebSocketOutboundMessages < OLD.WebSocketOutboundMessages OR
       NEW.WebSocketInboundBytes < OLD.WebSocketInboundBytes OR
       NEW.WebSocketOutboundBytes < OLD.WebSocketOutboundBytes OR
       NEW.AudioInputBytes < OLD.AudioInputBytes OR
       NEW.FirstEventUtc > OLD.FirstEventUtc OR
       NEW.LastEventUtc < OLD.LastEventUtc OR
       NEW.Revision < OLD.Revision OR
       NEW.NextSourceSequence < OLD.NextSourceSequence OR
       NEW.LastScheduledRevision < OLD.LastScheduledRevision OR
       (OLD.IsIncomplete AND NOT NEW.IsIncomplete) OR
       (OLD.FirstIncompleteUtc IS NOT NULL AND
           OLD.FirstIncompleteUtc IS DISTINCT FROM NEW.FirstIncompleteUtc) OR
       (OLD.IncompleteReasonCode IS NOT NULL AND
           OLD.IncompleteReasonCode IS DISTINCT FROM NEW.IncompleteReasonCode) THEN
        RAISE EXCEPTION 'runtime usage accumulator history cannot move backward'
            USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS TR_RuntimeUsageDailyAccumulators_Guard
    ON RuntimeUsageDailyAccumulators;
CREATE TRIGGER TR_RuntimeUsageDailyAccumulators_Guard
    BEFORE UPDATE OR DELETE ON RuntimeUsageDailyAccumulators
    FOR EACH ROW EXECUTE FUNCTION GuardRuntimeUsageAccumulatorMutation();

CREATE OR REPLACE FUNCTION RejectRuntimeUsageAppliedEventMutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'runtime usage applied events are immutable'
        USING ERRCODE = '55000';
END;
$$;

DROP TRIGGER IF EXISTS TR_RuntimeUsageAppliedEvents_Immutable
    ON RuntimeUsageAppliedEvents;
CREATE TRIGGER TR_RuntimeUsageAppliedEvents_Immutable
    BEFORE UPDATE OR DELETE ON RuntimeUsageAppliedEvents
    FOR EACH ROW EXECUTE FUNCTION RejectRuntimeUsageAppliedEventMutation();

CREATE OR REPLACE FUNCTION RecordRuntimeUsageEvent(
    p_source_subject_hmac BYTEA,
    p_usage_event_id UUID,
    p_occurred_utc TIMESTAMPTZ,
    p_service_environment TEXT,
    p_successful_turns BIGINT,
    p_failed_turns BIGINT,
    p_http_requests BIGINT,
    p_http_request_bytes BIGINT,
    p_http_response_bytes BIGINT,
    p_websocket_inbound_messages BIGINT,
    p_websocket_outbound_messages BIGINT,
    p_websocket_inbound_bytes BIGINT,
    p_websocket_outbound_bytes BIGINT,
    p_audio_input_bytes BIGINT,
    p_incomplete_reason_code TEXT DEFAULT NULL)
RETURNS TABLE
(
    ResolvedManagedRobotId UUID,
    ResolvedUsageDate DATE,
    AccumulatorRevision BIGINT,
    WasReplay BOOLEAN
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_robot_id UUID;
    v_usage_date DATE := (p_occurred_utc AT TIME ZONE 'UTC')::DATE;
    v_revision BIGINT;
    v_inserted INTEGER;
    v_existing RuntimeUsageAppliedEvents%ROWTYPE;
BEGIN
    IF p_source_subject_hmac IS NULL OR OCTET_LENGTH(p_source_subject_hmac) <> 32 OR
       p_usage_event_id IS NULL OR p_occurred_utc IS NULL OR
       p_service_environment IS NULL OR
       p_successful_turns IS NULL OR p_failed_turns IS NULL OR p_http_requests IS NULL OR
       p_http_request_bytes IS NULL OR p_http_response_bytes IS NULL OR
       p_websocket_inbound_messages IS NULL OR p_websocket_outbound_messages IS NULL OR
       p_websocket_inbound_bytes IS NULL OR p_websocket_outbound_bytes IS NULL OR
       p_audio_input_bytes IS NULL OR
       p_usage_event_id = '00000000-0000-0000-0000-000000000000' OR
       p_service_environment !~ '^[a-z][a-z0-9-]{0,31}$' OR
       p_occurred_utc < NOW() - INTERVAL '35 days' OR
       p_occurred_utc > NOW() + INTERVAL '5 minutes' OR
       p_successful_turns < 0 OR p_failed_turns < 0 OR p_http_requests < 0 OR
       p_http_request_bytes < 0 OR p_http_response_bytes < 0 OR
       p_websocket_inbound_messages < 0 OR p_websocket_outbound_messages < 0 OR
       p_websocket_inbound_bytes < 0 OR p_websocket_outbound_bytes < 0 OR
       p_audio_input_bytes < 0 OR
       p_successful_turns > 1 OR p_failed_turns > 1 OR
       p_successful_turns + p_failed_turns > 1 OR p_http_requests > 1 OR
       p_websocket_inbound_messages > 10000 OR p_websocket_outbound_messages > 10000 OR
       p_http_request_bytes > 1073741824 OR p_http_response_bytes > 1073741824 OR
       p_websocket_inbound_bytes > 1073741824 OR
       p_websocket_outbound_bytes > 1073741824 OR p_audio_input_bytes > 1073741824 OR
       (p_incomplete_reason_code IS NULL AND p_successful_turns = 0 AND
        p_failed_turns = 0 AND p_http_requests = 0 AND p_http_request_bytes = 0 AND
        p_http_response_bytes = 0 AND p_websocket_inbound_messages = 0 AND
        p_websocket_outbound_messages = 0 AND p_websocket_inbound_bytes = 0 AND
        p_websocket_outbound_bytes = 0 AND p_audio_input_bytes = 0) OR
       (p_incomplete_reason_code IS NOT NULL AND
        p_incomplete_reason_code !~ '^[a-z][a-z0-9-]{0,63}$') THEN
        RAISE EXCEPTION 'runtime usage event is invalid' USING ERRCODE = '22023';
    END IF;

    BEGIN
        SELECT binding.ManagedRobotId
        INTO STRICT v_robot_id
        FROM RuntimeUsageRobotBindings AS binding
        WHERE binding.SourceSubjectHmac = p_source_subject_hmac
          AND binding.ActiveFromUtc <= p_occurred_utc
          AND (binding.RevokedUtc IS NULL OR binding.RevokedUtc > p_occurred_utc);
    EXCEPTION
        WHEN NO_DATA_FOUND OR TOO_MANY_ROWS THEN
            RAISE EXCEPTION 'runtime usage identity binding is missing or ambiguous'
                USING ERRCODE = '22023';
    END;

    INSERT INTO RuntimeUsageAppliedEvents
        (UsageEventId, ManagedRobotId, UsageDate, ServiceEnvironment, OccurredUtc,
         SuccessfulTurnsDelta, FailedTurnsDelta, HttpRequestsDelta,
         HttpRequestBytesDelta, HttpResponseBytesDelta,
         WebSocketInboundMessagesDelta, WebSocketOutboundMessagesDelta,
         WebSocketInboundBytesDelta, WebSocketOutboundBytesDelta, AudioInputBytesDelta,
         IncompleteReasonCode)
    VALUES
        (p_usage_event_id, v_robot_id, v_usage_date, p_service_environment, p_occurred_utc,
         p_successful_turns, p_failed_turns, p_http_requests,
         p_http_request_bytes, p_http_response_bytes,
         p_websocket_inbound_messages, p_websocket_outbound_messages,
         p_websocket_inbound_bytes, p_websocket_outbound_bytes, p_audio_input_bytes,
         p_incomplete_reason_code)
    ON CONFLICT (UsageEventId) DO NOTHING;
    GET DIAGNOSTICS v_inserted = ROW_COUNT;

    IF v_inserted = 0 THEN
        SELECT * INTO STRICT v_existing
        FROM RuntimeUsageAppliedEvents
        WHERE UsageEventId = p_usage_event_id;
        IF v_existing.ManagedRobotId IS DISTINCT FROM v_robot_id OR
           v_existing.UsageDate IS DISTINCT FROM v_usage_date OR
           v_existing.ServiceEnvironment IS DISTINCT FROM p_service_environment OR
           v_existing.OccurredUtc IS DISTINCT FROM p_occurred_utc OR
           v_existing.SuccessfulTurnsDelta IS DISTINCT FROM p_successful_turns OR
           v_existing.FailedTurnsDelta IS DISTINCT FROM p_failed_turns OR
           v_existing.HttpRequestsDelta IS DISTINCT FROM p_http_requests OR
           v_existing.HttpRequestBytesDelta IS DISTINCT FROM p_http_request_bytes OR
           v_existing.HttpResponseBytesDelta IS DISTINCT FROM p_http_response_bytes OR
           v_existing.WebSocketInboundMessagesDelta IS DISTINCT FROM p_websocket_inbound_messages OR
           v_existing.WebSocketOutboundMessagesDelta IS DISTINCT FROM p_websocket_outbound_messages OR
           v_existing.WebSocketInboundBytesDelta IS DISTINCT FROM p_websocket_inbound_bytes OR
           v_existing.WebSocketOutboundBytesDelta IS DISTINCT FROM p_websocket_outbound_bytes OR
           v_existing.AudioInputBytesDelta IS DISTINCT FROM p_audio_input_bytes OR
           v_existing.IncompleteReasonCode IS DISTINCT FROM p_incomplete_reason_code THEN
            RAISE EXCEPTION 'runtime usage event idempotency conflict' USING ERRCODE = '22023';
        END IF;
        SELECT accumulator.Revision INTO STRICT v_revision
        FROM RuntimeUsageDailyAccumulators AS accumulator
        WHERE accumulator.ManagedRobotId = v_robot_id
          AND accumulator.UsageDate = v_usage_date
          AND accumulator.ServiceEnvironment = p_service_environment;
        RETURN QUERY SELECT v_robot_id, v_usage_date, v_revision, TRUE;
        RETURN;
    END IF;

    INSERT INTO RuntimeUsageDailyAccumulators AS accumulator
        (ManagedRobotId, UsageDate, ServiceEnvironment,
         SuccessfulTurns, FailedTurns, HttpRequests, HttpRequestBytes, HttpResponseBytes,
         WebSocketInboundMessages, WebSocketOutboundMessages,
         WebSocketInboundBytes, WebSocketOutboundBytes, AudioInputBytes,
         FirstEventUtc, LastEventUtc, Revision,
         IsIncomplete, FirstIncompleteUtc, IncompleteReasonCode)
    VALUES
        (v_robot_id, v_usage_date, p_service_environment,
         p_successful_turns, p_failed_turns, p_http_requests,
         p_http_request_bytes, p_http_response_bytes,
         p_websocket_inbound_messages, p_websocket_outbound_messages,
         p_websocket_inbound_bytes, p_websocket_outbound_bytes, p_audio_input_bytes,
         p_occurred_utc, p_occurred_utc, 1,
         p_incomplete_reason_code IS NOT NULL,
         CASE WHEN p_incomplete_reason_code IS NOT NULL THEN p_occurred_utc END,
         p_incomplete_reason_code)
    ON CONFLICT (ManagedRobotId, UsageDate, ServiceEnvironment) DO UPDATE SET
        SuccessfulTurns = accumulator.SuccessfulTurns + EXCLUDED.SuccessfulTurns,
        FailedTurns = accumulator.FailedTurns + EXCLUDED.FailedTurns,
        HttpRequests = accumulator.HttpRequests + EXCLUDED.HttpRequests,
        HttpRequestBytes = accumulator.HttpRequestBytes + EXCLUDED.HttpRequestBytes,
        HttpResponseBytes = accumulator.HttpResponseBytes + EXCLUDED.HttpResponseBytes,
        WebSocketInboundMessages = accumulator.WebSocketInboundMessages + EXCLUDED.WebSocketInboundMessages,
        WebSocketOutboundMessages = accumulator.WebSocketOutboundMessages + EXCLUDED.WebSocketOutboundMessages,
        WebSocketInboundBytes = accumulator.WebSocketInboundBytes + EXCLUDED.WebSocketInboundBytes,
        WebSocketOutboundBytes = accumulator.WebSocketOutboundBytes + EXCLUDED.WebSocketOutboundBytes,
        AudioInputBytes = accumulator.AudioInputBytes + EXCLUDED.AudioInputBytes,
        FirstEventUtc = LEAST(accumulator.FirstEventUtc, EXCLUDED.FirstEventUtc),
        LastEventUtc = GREATEST(accumulator.LastEventUtc, EXCLUDED.LastEventUtc),
        Revision = accumulator.Revision + 1,
        IsIncomplete = accumulator.IsIncomplete OR EXCLUDED.IsIncomplete,
        FirstIncompleteUtc = COALESCE(accumulator.FirstIncompleteUtc, EXCLUDED.FirstIncompleteUtc),
        IncompleteReasonCode = COALESCE(accumulator.IncompleteReasonCode, EXCLUDED.IncompleteReasonCode),
        UpdatedUtc = NOW()
    RETURNING Revision INTO v_revision;

    RETURN QUERY SELECT v_robot_id, v_usage_date, v_revision, FALSE;
END;
$$;

CREATE INDEX IF NOT EXISTS IX_RuntimeUsageAppliedEvents_Stream
    ON RuntimeUsageAppliedEvents
        (ManagedRobotId, UsageDate, ServiceEnvironment, AppliedUtc, UsageEventId);

CREATE TABLE IF NOT EXISTS RuntimeUsageOutboxMessages
(
    MessageId UUID NOT NULL PRIMARY KEY,
    ManagedRobotId UUID NOT NULL,
    UsageDate DATE NOT NULL,
    ServiceEnvironment TEXT NOT NULL,
    SourceSequence BIGINT NOT NULL CHECK (SourceSequence > 0),
    AccumulatorRevision BIGINT NOT NULL CHECK (AccumulatorRevision > 0),
    FormatVersion SMALLINT NOT NULL CHECK (FormatVersion = 1),
    SourceSchemaVersion TEXT NOT NULL CHECK (SourceSchemaVersion = 'openjibo-runtime-usage.v1'),
    SourceRevision TEXT NOT NULL CHECK (SourceRevision ~ '^[1-9][0-9]{0,18}$'),
    SuccessfulTurns BIGINT NOT NULL CHECK (SuccessfulTurns >= 0),
    FailedTurns BIGINT NOT NULL CHECK (FailedTurns >= 0),
    HttpRequests BIGINT NOT NULL CHECK (HttpRequests >= 0),
    HttpRequestBytes BIGINT NOT NULL CHECK (HttpRequestBytes >= 0),
    HttpResponseBytes BIGINT NOT NULL CHECK (HttpResponseBytes >= 0),
    WebSocketInboundMessages BIGINT NOT NULL CHECK (WebSocketInboundMessages >= 0),
    WebSocketOutboundMessages BIGINT NOT NULL CHECK (WebSocketOutboundMessages >= 0),
    WebSocketInboundBytes BIGINT NOT NULL CHECK (WebSocketInboundBytes >= 0),
    WebSocketOutboundBytes BIGINT NOT NULL CHECK (WebSocketOutboundBytes >= 0),
    AudioInputBytes BIGINT NOT NULL CHECK (AudioInputBytes >= 0),
    FirstEventUtc TIMESTAMPTZ NOT NULL,
    LastEventUtc TIMESTAMPTZ NOT NULL,
    IsIncomplete BOOLEAN NOT NULL,
    FirstIncompleteUtc TIMESTAMPTZ NULL,
    IncompleteReasonCode TEXT NULL,
    IdempotencyKey TEXT NOT NULL
        CHECK (CHAR_LENGTH(IdempotencyKey) = 43 AND
               IdempotencyKey ~ '^[A-Za-z0-9_-]+$'),
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    FOREIGN KEY (ManagedRobotId, UsageDate, ServiceEnvironment)
        REFERENCES RuntimeUsageDailyAccumulators
            (ManagedRobotId, UsageDate, ServiceEnvironment),
    UNIQUE (ManagedRobotId, UsageDate, ServiceEnvironment, SourceSequence),
    UNIQUE (IdempotencyKey),
    CHECK (FirstEventUtc <= LastEventUtc),
    CHECK ((FirstEventUtc AT TIME ZONE 'UTC')::DATE = UsageDate),
    CHECK ((LastEventUtc AT TIME ZONE 'UTC')::DATE = UsageDate),
    CHECK (
        IsIncomplete OR SuccessfulTurns > 0 OR FailedTurns > 0 OR HttpRequests > 0 OR
        HttpRequestBytes > 0 OR HttpResponseBytes > 0 OR
        WebSocketInboundMessages > 0 OR WebSocketOutboundMessages > 0 OR
        WebSocketInboundBytes > 0 OR WebSocketOutboundBytes > 0 OR AudioInputBytes > 0
    ),
    CHECK (
        (IsIncomplete AND FirstIncompleteUtc IS NOT NULL AND
            IncompleteReasonCode ~ '^[a-z][a-z0-9-]{0,63}$') OR
        (NOT IsIncomplete AND FirstIncompleteUtc IS NULL AND IncompleteReasonCode IS NULL)
    )
);

CREATE INDEX IF NOT EXISTS IX_RuntimeUsageOutboxMessages_Stream
    ON RuntimeUsageOutboxMessages
        (ManagedRobotId, UsageDate, ServiceEnvironment, SourceSequence);

CREATE OR REPLACE FUNCTION RejectRuntimeUsageOutboxMessageMutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'runtime usage outbox messages are immutable'
        USING ERRCODE = '55000';
END;
$$;

DROP TRIGGER IF EXISTS TR_RuntimeUsageOutboxMessages_Immutable
    ON RuntimeUsageOutboxMessages;
CREATE TRIGGER TR_RuntimeUsageOutboxMessages_Immutable
    BEFORE UPDATE OR DELETE ON RuntimeUsageOutboxMessages
    FOR EACH ROW EXECUTE FUNCTION RejectRuntimeUsageOutboxMessageMutation();

CREATE TABLE IF NOT EXISTS RuntimeUsageOutboxDelivery
(
    MessageId UUID NOT NULL PRIMARY KEY
        REFERENCES RuntimeUsageOutboxMessages (MessageId),
    DeliveryState TEXT NOT NULL DEFAULT 'pending'
        CHECK (DeliveryState IN ('pending', 'leased', 'acknowledged', 'quarantined')),
    AttemptCount INTEGER NOT NULL DEFAULT 0 CHECK (AttemptCount BETWEEN 0 AND 100000),
    NotBeforeUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    LeaseOwner TEXT NULL CHECK (
        LeaseOwner IS NULL OR LeaseOwner ~ '^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,127}$'),
    LeaseExpiresUtc TIMESTAMPTZ NULL,
    LastFailureCategory TEXT NULL CHECK (
        LastFailureCategory IS NULL OR LastFailureCategory ~ '^[a-z][a-z0-9-]{0,63}$'),
    AcknowledgedUtc TIMESTAMPTZ NULL,
    ReceiptHash BYTEA NULL CHECK (ReceiptHash IS NULL OR OCTET_LENGTH(ReceiptHash) = 32),
    QuarantinedUtc TIMESTAMPTZ NULL,
    QuarantineCategory TEXT NULL CHECK (
        QuarantineCategory IS NULL OR QuarantineCategory ~ '^[a-z][a-z0-9-]{0,63}$'),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (
        (DeliveryState = 'pending' AND LeaseOwner IS NULL AND LeaseExpiresUtc IS NULL AND
            AcknowledgedUtc IS NULL AND ReceiptHash IS NULL AND
            QuarantinedUtc IS NULL AND QuarantineCategory IS NULL) OR
        (DeliveryState = 'leased' AND LeaseOwner IS NOT NULL AND LeaseExpiresUtc IS NOT NULL AND
            AcknowledgedUtc IS NULL AND ReceiptHash IS NULL AND
            QuarantinedUtc IS NULL AND QuarantineCategory IS NULL) OR
        (DeliveryState = 'acknowledged' AND LeaseOwner IS NULL AND LeaseExpiresUtc IS NULL AND
            AcknowledgedUtc IS NOT NULL AND ReceiptHash IS NOT NULL AND
            QuarantinedUtc IS NULL AND QuarantineCategory IS NULL) OR
        (DeliveryState = 'quarantined' AND LeaseOwner IS NULL AND LeaseExpiresUtc IS NULL AND
            AcknowledgedUtc IS NULL AND ReceiptHash IS NULL AND
            QuarantinedUtc IS NOT NULL AND QuarantineCategory IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS IX_RuntimeUsageOutboxDelivery_Ready
    ON RuntimeUsageOutboxDelivery (DeliveryState, NotBeforeUtc, MessageId)
    WHERE DeliveryState IN ('pending', 'leased');

CREATE OR REPLACE FUNCTION GuardRuntimeUsageOutboxDeliveryMutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' OR
       OLD.MessageId IS DISTINCT FROM NEW.MessageId OR
       NEW.AttemptCount < OLD.AttemptCount OR
       OLD.DeliveryState IN ('acknowledged', 'quarantined') OR
       (OLD.DeliveryState = 'pending' AND NEW.DeliveryState NOT IN ('pending', 'leased', 'quarantined')) OR
       (OLD.DeliveryState = 'leased' AND NEW.DeliveryState NOT IN
            ('pending', 'leased', 'acknowledged', 'quarantined')) OR
       (OLD.DeliveryState = 'leased' AND NEW.DeliveryState = 'pending' AND
            OLD.LeaseExpiresUtc > NOW()) THEN
        RAISE EXCEPTION 'runtime usage delivery state transition is invalid'
            USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS TR_RuntimeUsageOutboxDelivery_Guard
    ON RuntimeUsageOutboxDelivery;
CREATE TRIGGER TR_RuntimeUsageOutboxDelivery_Guard
    BEFORE UPDATE OR DELETE ON RuntimeUsageOutboxDelivery
    FOR EACH ROW EXECUTE FUNCTION GuardRuntimeUsageOutboxDeliveryMutation();

CREATE OR REPLACE FUNCTION ScheduleRuntimeUsageSnapshot(
    p_source_subject_hmac BYTEA,
    p_usage_date DATE,
    p_service_environment TEXT,
    p_message_id UUID,
    p_idempotency_key TEXT)
RETURNS TABLE
(
    ScheduledMessageId UUID,
    ScheduledSourceSequence BIGINT,
    ScheduledAccumulatorRevision BIGINT,
    WasCreated BOOLEAN
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_robot_id UUID;
    v_accumulator RuntimeUsageDailyAccumulators%ROWTYPE;
    v_existing RuntimeUsageOutboxMessages%ROWTYPE;
    v_day_start TIMESTAMPTZ := p_usage_date::TIMESTAMP AT TIME ZONE 'UTC';
    v_day_end TIMESTAMPTZ := (p_usage_date + 1)::TIMESTAMP AT TIME ZONE 'UTC';
    v_existing_found BOOLEAN := FALSE;
BEGIN
    IF p_source_subject_hmac IS NULL OR OCTET_LENGTH(p_source_subject_hmac) <> 32 OR
       p_usage_date IS NULL OR p_service_environment IS NULL OR
       p_message_id IS NULL OR p_idempotency_key IS NULL OR
       p_service_environment !~ '^[a-z][a-z0-9-]{0,31}$' OR
       p_usage_date < (NOW() AT TIME ZONE 'UTC')::DATE - 35 OR
       p_usage_date > (NOW() AT TIME ZONE 'UTC')::DATE + 1 OR
       p_message_id = '00000000-0000-0000-0000-000000000000' OR
       CHAR_LENGTH(p_idempotency_key) <> 43 OR
       p_idempotency_key !~ '^[A-Za-z0-9_-]+$' THEN
        RAISE EXCEPTION 'runtime usage snapshot request is invalid' USING ERRCODE = '22023';
    END IF;

    BEGIN
        SELECT binding.ManagedRobotId
        INTO STRICT v_robot_id
        FROM RuntimeUsageRobotBindings AS binding
        WHERE binding.SourceSubjectHmac = p_source_subject_hmac
          AND binding.ActiveFromUtc < v_day_end
          AND (binding.RevokedUtc IS NULL OR binding.RevokedUtc > v_day_start);
    EXCEPTION
        WHEN NO_DATA_FOUND OR TOO_MANY_ROWS THEN
            RAISE EXCEPTION 'runtime usage identity binding is missing or ambiguous'
                USING ERRCODE = '22023';
    END;

    SELECT * INTO STRICT v_accumulator
    FROM RuntimeUsageDailyAccumulators AS accumulator
    WHERE accumulator.ManagedRobotId = v_robot_id
      AND accumulator.UsageDate = p_usage_date
      AND accumulator.ServiceEnvironment = p_service_environment
    FOR UPDATE;

    BEGIN
        SELECT * INTO STRICT v_existing
        FROM RuntimeUsageOutboxMessages AS message
        WHERE message.MessageId = p_message_id
           OR message.IdempotencyKey = p_idempotency_key;
        v_existing_found := TRUE;
    EXCEPTION
        WHEN NO_DATA_FOUND THEN
            v_existing_found := FALSE;
        WHEN TOO_MANY_ROWS THEN
            RAISE EXCEPTION 'runtime usage snapshot idempotency conflict'
                USING ERRCODE = '22023';
    END;

    IF v_existing_found THEN
        IF v_existing.MessageId IS DISTINCT FROM p_message_id OR
           v_existing.IdempotencyKey IS DISTINCT FROM p_idempotency_key OR
           v_existing.ManagedRobotId IS DISTINCT FROM v_robot_id OR
           v_existing.UsageDate IS DISTINCT FROM p_usage_date OR
           v_existing.ServiceEnvironment IS DISTINCT FROM p_service_environment THEN
            RAISE EXCEPTION 'runtime usage snapshot idempotency conflict'
                USING ERRCODE = '22023';
        END IF;
        RETURN QUERY SELECT v_existing.MessageId, v_existing.SourceSequence,
            v_existing.AccumulatorRevision, FALSE;
        RETURN;
    END IF;

    IF v_accumulator.Revision <= v_accumulator.LastScheduledRevision THEN
        RAISE EXCEPTION 'runtime usage accumulator has no unscheduled revision'
            USING ERRCODE = '22023';
    END IF;

    INSERT INTO RuntimeUsageOutboxMessages
        (MessageId, ManagedRobotId, UsageDate, ServiceEnvironment, SourceSequence,
         AccumulatorRevision, FormatVersion,
         SourceSchemaVersion, SourceRevision,
         SuccessfulTurns, FailedTurns, HttpRequests, HttpRequestBytes, HttpResponseBytes,
         WebSocketInboundMessages, WebSocketOutboundMessages,
         WebSocketInboundBytes, WebSocketOutboundBytes, AudioInputBytes,
         FirstEventUtc, LastEventUtc, IsIncomplete, FirstIncompleteUtc, IncompleteReasonCode,
         IdempotencyKey)
    VALUES
         (p_message_id, v_robot_id, p_usage_date, p_service_environment,
         v_accumulator.NextSourceSequence, v_accumulator.Revision, 1,
         'openjibo-runtime-usage.v1', v_accumulator.Revision::TEXT,
         v_accumulator.SuccessfulTurns, v_accumulator.FailedTurns,
         v_accumulator.HttpRequests, v_accumulator.HttpRequestBytes, v_accumulator.HttpResponseBytes,
         v_accumulator.WebSocketInboundMessages, v_accumulator.WebSocketOutboundMessages,
         v_accumulator.WebSocketInboundBytes, v_accumulator.WebSocketOutboundBytes,
         v_accumulator.AudioInputBytes, v_accumulator.FirstEventUtc, v_accumulator.LastEventUtc,
         v_accumulator.IsIncomplete, v_accumulator.FirstIncompleteUtc,
         v_accumulator.IncompleteReasonCode, p_idempotency_key);

    INSERT INTO RuntimeUsageOutboxDelivery (MessageId) VALUES (p_message_id);

    UPDATE RuntimeUsageDailyAccumulators
    SET NextSourceSequence = v_accumulator.NextSourceSequence + 1,
        LastScheduledRevision = v_accumulator.Revision,
        UpdatedUtc = NOW()
    WHERE ManagedRobotId = v_robot_id
      AND UsageDate = p_usage_date
      AND ServiceEnvironment = p_service_environment;

    RETURN QUERY SELECT p_message_id, v_accumulator.NextSourceSequence,
        v_accumulator.Revision, TRUE;
END;
$$;

-- Storage only: this migration grants no application, binding-administrator, or collector role.
-- Activation remains blocked until narrowly owned SECURITY DEFINER wrappers and separate
-- binding-administration, head-of-line claim, acknowledge, and quarantine functions are reviewed and tested.

REVOKE ALL ON RuntimeUsageRobotBindings FROM PUBLIC;
REVOKE ALL ON RuntimeUsageDailyAccumulators FROM PUBLIC;
REVOKE ALL ON RuntimeUsageAppliedEvents FROM PUBLIC;
REVOKE ALL ON RuntimeUsageOutboxMessages FROM PUBLIC;
REVOKE ALL ON RuntimeUsageOutboxDelivery FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION GuardRuntimeUsageRobotBindingMutation() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION GuardRuntimeUsageAccumulatorMutation() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION RejectRuntimeUsageAppliedEventMutation() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION RecordRuntimeUsageEvent(BYTEA,UUID,TIMESTAMPTZ,TEXT,BIGINT,BIGINT,BIGINT,BIGINT,BIGINT,BIGINT,BIGINT,BIGINT,BIGINT,BIGINT,TEXT) FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION RejectRuntimeUsageOutboxMessageMutation() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION GuardRuntimeUsageOutboxDeliveryMutation() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION ScheduleRuntimeUsageSnapshot(BYTEA,DATE,TEXT,UUID,TEXT) FROM PUBLIC;
