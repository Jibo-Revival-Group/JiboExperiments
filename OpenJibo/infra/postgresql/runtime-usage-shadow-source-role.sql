-- Dormant, read-only runtime usage shadow-source boundary.
-- The provisioner replaces the configured state-schema placeholders. This
-- artifact never creates a login, password, credential, or worker.

SELECT pg_catalog.pg_advisory_xact_lock(hashtextextended('openjibo-runtime-usage-shadow-source-role', 0));

DO $preflight$
DECLARE login_oid oid; owner_oid oid; capability_oid oid; admin_oid oid := (SELECT oid FROM pg_roles WHERE rolname=current_user); schema_owner oid; state_schema_owner oid;
BEGIN
  IF '{{STATE_SCHEMA_LITERAL}}' IN ('runtime_usage_shadow_source','pg_catalog','information_schema','pg_temp') THEN RAISE EXCEPTION 'configured state schema crosses shadow-source boundary'; END IF;
  SELECT oid INTO login_oid FROM pg_catalog.pg_roles WHERE rolname='openjibo_runtime_usage_shadow_source';
  IF login_oid IS NULL THEN RAISE EXCEPTION 'openjibo_runtime_usage_shadow_source must be pre-created'; END IF;
  IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid=login_oid AND (NOT rolcanlogin OR NOT rolinherit OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls OR rolconnlimit<>1)) THEN RAISE EXCEPTION 'shadow-source login attributes are unsafe'; END IF;
  IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m JOIN pg_catalog.pg_roles r ON r.oid=m.roleid WHERE m.member=login_oid AND r.rolname<>'openjibo_runtime_usage_shadow_source_reader') THEN RAISE EXCEPTION 'shadow-source login has unexpected membership'; END IF;
  IF pg_catalog.has_database_privilege('openjibo_runtime_usage_shadow_source',current_database(),'CREATE') OR pg_catalog.has_database_privilege('openjibo_runtime_usage_shadow_source',current_database(),'TEMPORARY') THEN RAISE EXCEPTION 'shadow-source login has unsafe database privileges'; END IF;
  IF EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(d.datacl,pg_catalog.acldefault('d',d.datdba))) a WHERE d.datname=current_database() AND a.grantee=0 AND a.privilege_type='TEMPORARY') THEN RAISE EXCEPTION 'database PUBLIC temporary privilege must be revoked before shadow-source activation'; END IF;
  SELECT oid INTO owner_oid FROM pg_catalog.pg_roles WHERE rolname='openjibo_runtime_usage_shadow_source_owner';
  SELECT oid INTO capability_oid FROM pg_catalog.pg_roles WHERE rolname='openjibo_runtime_usage_shadow_source_reader';
  IF owner_oid IS NOT NULL AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid=owner_oid AND (rolcanlogin OR rolinherit OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) THEN RAISE EXCEPTION 'shadow-source owner attributes are unsafe'; END IF;
  IF capability_oid IS NOT NULL AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid=capability_oid AND (rolcanlogin OR NOT rolinherit OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) THEN RAISE EXCEPTION 'shadow-source capability attributes are unsafe'; END IF;
  -- PostgreSQL grants a non-superuser role creator a bootstrap-owned ADMIN edge
  -- with neither INHERIT nor SET. It cannot be revoked by that creator.
  IF EXISTS (
    SELECT 1 FROM pg_catalog.pg_auth_members m
    WHERE (m.member IN (login_oid,owner_oid,capability_oid) OR m.roleid IN (login_oid,owner_oid,capability_oid))
      AND NOT (
        (m.member=admin_oid AND m.roleid IN (login_oid,owner_oid,capability_oid)
          AND m.grantor=10::oid AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles b WHERE b.oid=10 AND b.rolsuper)
          AND m.admin_option AND NOT m.inherit_option AND NOT m.set_option)
        OR (m.member=admin_oid AND m.roleid=owner_oid AND m.grantor=admin_oid
          AND NOT m.admin_option AND NOT m.inherit_option AND m.set_option)
        OR (m.member=login_oid AND m.roleid=capability_oid AND m.grantor=admin_oid
          AND NOT m.admin_option AND m.inherit_option AND NOT m.set_option)
      )
  ) THEN RAISE EXCEPTION 'shadow-source supporting roles have unsafe membership'; END IF;
  SELECT nspowner INTO schema_owner FROM pg_catalog.pg_namespace WHERE nspname='runtime_usage_shadow_source';
  IF schema_owner IS NOT NULL AND schema_owner<>admin_oid THEN RAISE EXCEPTION 'shadow-source wrapper schema must remain deployment-administrator owned'; END IF;
  IF EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(n.nspacl,pg_catalog.acldefault('n',n.nspowner))) a WHERE n.nspname='runtime_usage_shadow_source' AND a.grantee=0) THEN RAISE EXCEPTION 'shadow-source wrapper schema has unsafe PUBLIC privileges'; END IF;
  SELECT nspowner INTO state_schema_owner FROM pg_catalog.pg_namespace WHERE nspname='{{STATE_SCHEMA_LITERAL}}';
  IF state_schema_owner IN (login_oid,owner_oid,capability_oid) THEN RAISE EXCEPTION 'shadow-source raw schema ownership is contaminated'; END IF;
  IF EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(n.nspacl,pg_catalog.acldefault('n',n.nspowner))) a WHERE n.nspname='{{STATE_SCHEMA_LITERAL}}' AND a.grantee=0 AND a.privilege_type='CREATE') THEN RAISE EXCEPTION 'configured state schema grants CREATE to PUBLIC'; END IF;
  IF EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(n.nspacl,pg_catalog.acldefault('n',n.nspowner))) a WHERE n.nspname='{{STATE_SCHEMA_LITERAL}}' AND a.grantee IN (login_oid,capability_oid,owner_oid) AND NOT (a.grantee=owner_oid AND a.privilege_type='USAGE')) THEN RAISE EXCEPTION 'shadow-source raw schema ACL is contaminated'; END IF;
  IF EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(c.relacl,pg_catalog.acldefault(CASE WHEN c.relkind='S' THEN 'S'::"char" ELSE 'r'::"char" END,c.relowner))) a WHERE n.nspname='{{STATE_SCHEMA_LITERAL}}' AND c.relkind IN ('r','p','v','m','S','f') AND a.grantee IN (0,login_oid,capability_oid,owner_oid) AND NOT (a.grantee=owner_oid AND c.relkind IN ('r','p') AND c.relname IN ('runtimeusageoutboxmessages','runtimeusageoutboxdelivery') AND a.privilege_type='SELECT')) THEN RAISE EXCEPTION 'shadow-source raw relation ACL is contaminated'; END IF;
  IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(p.proacl,pg_catalog.acldefault('f',p.proowner))) a WHERE n.nspname='{{STATE_SCHEMA_LITERAL}}' AND a.grantee IN (0,login_oid,capability_oid,owner_oid)) THEN RAISE EXCEPTION 'shadow-source raw function ACL is contaminated'; END IF;
  IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='runtime_usage_shadow_source' AND p.oid IS DISTINCT FROM to_regprocedure('runtime_usage_shadow_source.describe_runtime_usage_stream(uuid,date,text)') AND p.oid IS DISTINCT FROM to_regprocedure('runtime_usage_shadow_source.read_runtime_usage_stream_page(uuid,date,text,bigint,bigint,integer)')) THEN RAISE EXCEPTION 'shadow-source wrapper schema contains an unexpected function'; END IF;
END
$preflight$;

DO $roles$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname='openjibo_runtime_usage_shadow_source_owner') THEN CREATE ROLE openjibo_runtime_usage_shadow_source_owner NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS; END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname='openjibo_runtime_usage_shadow_source_reader') THEN CREATE ROLE openjibo_runtime_usage_shadow_source_reader NOLOGIN INHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS; END IF;
END
$roles$;
ALTER ROLE openjibo_runtime_usage_shadow_source_owner NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
ALTER ROLE openjibo_runtime_usage_shadow_source_reader NOLOGIN INHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;

CREATE SCHEMA IF NOT EXISTS runtime_usage_shadow_source;
REVOKE ALL ON SCHEMA runtime_usage_shadow_source FROM PUBLIC;
GRANT openjibo_runtime_usage_shadow_source_owner TO CURRENT_USER WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
REVOKE ALL ON SCHEMA {{STATE_SCHEMA_IDENTIFIER}} FROM openjibo_runtime_usage_shadow_source,openjibo_runtime_usage_shadow_source_reader,openjibo_runtime_usage_shadow_source_owner;
REVOKE ALL ON TABLE {{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxMessages,{{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxDelivery FROM openjibo_runtime_usage_shadow_source,openjibo_runtime_usage_shadow_source_reader,openjibo_runtime_usage_shadow_source_owner;
DO $database_acl$ BEGIN EXECUTE format('REVOKE CREATE, TEMPORARY ON DATABASE %I FROM openjibo_runtime_usage_shadow_source,openjibo_runtime_usage_shadow_source_reader,openjibo_runtime_usage_shadow_source_owner',current_database()); END $database_acl$;

GRANT USAGE ON SCHEMA {{STATE_SCHEMA_IDENTIFIER}} TO openjibo_runtime_usage_shadow_source_owner;
GRANT SELECT ON TABLE {{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxMessages,{{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxDelivery TO openjibo_runtime_usage_shadow_source_owner;
GRANT USAGE,CREATE ON SCHEMA runtime_usage_shadow_source TO openjibo_runtime_usage_shadow_source_owner;
SET LOCAL ROLE openjibo_runtime_usage_shadow_source_owner;
DROP FUNCTION IF EXISTS runtime_usage_shadow_source.describe_runtime_usage_stream(uuid,date,text);
DROP FUNCTION IF EXISTS runtime_usage_shadow_source.read_runtime_usage_stream_page(uuid,date,text,bigint,bigint,integer);

CREATE FUNCTION runtime_usage_shadow_source.describe_runtime_usage_stream(p_robot_id uuid,p_usage_date date,p_service_environment text)
RETURNS TABLE(observed_utc timestamptz,message_high_watermark_sequence bigint,stream_exists boolean)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,{{STATE_SCHEMA_IDENTIFIER}},pg_temp AS $fn$
BEGIN
  IF p_robot_id IS NULL OR p_robot_id='00000000-0000-0000-0000-000000000000' OR p_usage_date IS NULL OR p_service_environment IS NULL OR p_service_environment !~ '^[a-z][a-z0-9-]{0,31}$' THEN RAISE EXCEPTION 'runtime usage shadow stream request is invalid' USING ERRCODE='22023'; END IF;
  RETURN QUERY SELECT statement_timestamp(),COALESCE(MAX(m.SourceSequence),0),COUNT(*)<>0 FROM RuntimeUsageOutboxMessages m WHERE m.ManagedRobotId=p_robot_id AND m.UsageDate=p_usage_date AND m.ServiceEnvironment=p_service_environment;
END $fn$;

CREATE FUNCTION runtime_usage_shadow_source.read_runtime_usage_stream_page(p_robot_id uuid,p_usage_date date,p_service_environment text,p_after_sequence bigint,p_through_sequence bigint,p_page_size integer)
RETURNS TABLE(message_id uuid,managed_robot_id uuid,usage_date date,service_environment text,source_sequence bigint,accumulator_revision bigint,format_version smallint,source_schema_version text,source_revision text,successful_turns bigint,failed_turns bigint,http_requests bigint,http_request_bytes bigint,http_response_bytes bigint,websocket_inbound_messages bigint,websocket_outbound_messages bigint,websocket_inbound_bytes bigint,websocket_outbound_bytes bigint,audio_input_bytes bigint,first_event_utc timestamptz,last_event_utc timestamptz,is_incomplete boolean,first_incomplete_utc timestamptz,incomplete_reason_code text,idempotency_key text,created_utc timestamptz,delivery_state text,delivery_observed_utc timestamptz,acknowledgement_receipt_hash bytea,quarantine_category text,page_through_sequence bigint,is_complete boolean,observed_utc timestamptz)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,{{STATE_SCHEMA_IDENTIFIER}},pg_temp AS $fn$
DECLARE current_high bigint;
BEGIN
  IF p_robot_id IS NULL OR p_robot_id='00000000-0000-0000-0000-000000000000' OR p_usage_date IS NULL OR p_service_environment IS NULL OR p_service_environment !~ '^[a-z][a-z0-9-]{0,31}$' OR p_after_sequence IS NULL OR p_after_sequence<0 OR p_through_sequence IS NULL OR p_through_sequence<p_after_sequence OR p_page_size IS NULL OR p_page_size NOT BETWEEN 1 AND 250 THEN RAISE EXCEPTION 'runtime usage shadow page request is invalid' USING ERRCODE='22023'; END IF;
  SELECT COALESCE(MAX(SourceSequence),0) INTO current_high FROM RuntimeUsageOutboxMessages WHERE ManagedRobotId=p_robot_id AND UsageDate=p_usage_date AND ServiceEnvironment=p_service_environment;
  IF p_through_sequence>current_high THEN RAISE EXCEPTION 'runtime usage shadow watermark is invalid' USING ERRCODE='22023'; END IF;
  RETURN QUERY WITH page AS (SELECT m.*,d.DeliveryState,d.UpdatedUtc AS DeliveryObservedUtc,d.ReceiptHash,d.QuarantineCategory FROM RuntimeUsageOutboxMessages m JOIN RuntimeUsageOutboxDelivery d ON d.MessageId=m.MessageId WHERE m.ManagedRobotId=p_robot_id AND m.UsageDate=p_usage_date AND m.ServiceEnvironment=p_service_environment AND m.SourceSequence>p_after_sequence AND m.SourceSequence<=p_through_sequence ORDER BY m.SourceSequence LIMIT p_page_size), tail AS (SELECT COALESCE(MAX(SourceSequence),p_after_sequence) AS last_sequence FROM page) SELECT p.MessageId,p.ManagedRobotId,p.UsageDate,p.ServiceEnvironment,p.SourceSequence,p.AccumulatorRevision,p.FormatVersion,p.SourceSchemaVersion,p.SourceRevision,p.SuccessfulTurns,p.FailedTurns,p.HttpRequests,p.HttpRequestBytes,p.HttpResponseBytes,p.WebSocketInboundMessages,p.WebSocketOutboundMessages,p.WebSocketInboundBytes,p.WebSocketOutboundBytes,p.AudioInputBytes,p.FirstEventUtc,p.LastEventUtc,p.IsIncomplete,p.FirstIncompleteUtc,p.IncompleteReasonCode,p.IdempotencyKey,p.CreatedUtc,p.DeliveryState,p.DeliveryObservedUtc,p.ReceiptHash,p.QuarantineCategory,p_through_sequence,NOT EXISTS(SELECT 1 FROM RuntimeUsageOutboxMessages later WHERE later.ManagedRobotId=p_robot_id AND later.UsageDate=p_usage_date AND later.ServiceEnvironment=p_service_environment AND later.SourceSequence>(SELECT last_sequence FROM tail) AND later.SourceSequence<=p_through_sequence),statement_timestamp() FROM page p;
END $fn$;

ALTER FUNCTION runtime_usage_shadow_source.describe_runtime_usage_stream(uuid,date,text) OWNER TO openjibo_runtime_usage_shadow_source_owner;
ALTER FUNCTION runtime_usage_shadow_source.read_runtime_usage_stream_page(uuid,date,text,bigint,bigint,integer) OWNER TO openjibo_runtime_usage_shadow_source_owner;
RESET ROLE;
REVOKE CREATE ON SCHEMA runtime_usage_shadow_source FROM openjibo_runtime_usage_shadow_source_owner;
GRANT USAGE ON SCHEMA runtime_usage_shadow_source TO openjibo_runtime_usage_shadow_source_reader;
SET LOCAL ROLE openjibo_runtime_usage_shadow_source_owner;
REVOKE ALL ON FUNCTION runtime_usage_shadow_source.describe_runtime_usage_stream(uuid,date,text),runtime_usage_shadow_source.read_runtime_usage_stream_page(uuid,date,text,bigint,bigint,integer) FROM PUBLIC,openjibo_runtime_usage_shadow_source;
GRANT EXECUTE ON FUNCTION runtime_usage_shadow_source.describe_runtime_usage_stream(uuid,date,text),runtime_usage_shadow_source.read_runtime_usage_stream_page(uuid,date,text,bigint,bigint,integer) TO openjibo_runtime_usage_shadow_source_reader;
RESET ROLE;
GRANT openjibo_runtime_usage_shadow_source_reader TO openjibo_runtime_usage_shadow_source WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;

DO $verify$
DECLARE wrapper regprocedure; raw_relation record;
BEGIN
  IF pg_catalog.has_database_privilege('openjibo_runtime_usage_shadow_source',current_database(),'CREATE') OR pg_catalog.has_database_privilege('openjibo_runtime_usage_shadow_source',current_database(),'TEMPORARY') OR pg_catalog.has_schema_privilege('openjibo_runtime_usage_shadow_source','{{STATE_SCHEMA_LITERAL}}','USAGE') OR pg_catalog.has_schema_privilege('openjibo_runtime_usage_shadow_source','{{STATE_SCHEMA_LITERAL}}','CREATE') OR pg_catalog.has_schema_privilege('openjibo_runtime_usage_shadow_source_reader','{{STATE_SCHEMA_LITERAL}}','USAGE') OR pg_catalog.has_schema_privilege('openjibo_runtime_usage_shadow_source_reader','{{STATE_SCHEMA_LITERAL}}','CREATE') OR NOT pg_catalog.has_schema_privilege('openjibo_runtime_usage_shadow_source_owner','{{STATE_SCHEMA_LITERAL}}','USAGE') OR pg_catalog.has_schema_privilege('openjibo_runtime_usage_shadow_source_owner','{{STATE_SCHEMA_LITERAL}}','CREATE') OR pg_catalog.has_schema_privilege('openjibo_runtime_usage_shadow_source','runtime_usage_shadow_source','CREATE') OR NOT pg_catalog.has_schema_privilege('openjibo_runtime_usage_shadow_source','runtime_usage_shadow_source','USAGE') OR pg_catalog.has_schema_privilege('openjibo_runtime_usage_shadow_source_owner','runtime_usage_shadow_source','CREATE') OR pg_catalog.pg_has_role('openjibo_runtime_usage_shadow_source','openjibo_runtime_usage_shadow_source_reader','SET') THEN RAISE EXCEPTION 'shadow-source effective boundary is unsafe'; END IF;
  FOREACH wrapper IN ARRAY ARRAY[to_regprocedure('runtime_usage_shadow_source.describe_runtime_usage_stream(uuid,date,text)'),to_regprocedure('runtime_usage_shadow_source.read_runtime_usage_stream_page(uuid,date,text,bigint,bigint,integer)')] LOOP
    IF wrapper IS NULL OR NOT (SELECT prosecdef FROM pg_catalog.pg_proc WHERE oid=wrapper) OR (SELECT pg_catalog.pg_get_userbyid(proowner) FROM pg_catalog.pg_proc WHERE oid=wrapper)<>'openjibo_runtime_usage_shadow_source_owner' OR NOT pg_catalog.has_function_privilege('openjibo_runtime_usage_shadow_source',wrapper,'EXECUTE') OR NOT pg_catalog.has_function_privilege('openjibo_runtime_usage_shadow_source_reader',wrapper,'EXECUTE') THEN RAISE EXCEPTION 'shadow-source wrapper definition is unsafe'; END IF;
  END LOOP;
  FOR raw_relation IN SELECT c.oid FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='{{STATE_SCHEMA_LITERAL}}' AND c.relkind IN ('r','p','v','m','S','f') LOOP
    IF pg_catalog.has_table_privilege('openjibo_runtime_usage_shadow_source',raw_relation.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') OR pg_catalog.has_any_column_privilege('openjibo_runtime_usage_shadow_source',raw_relation.oid,'SELECT,INSERT,UPDATE,REFERENCES') OR pg_catalog.has_table_privilege('openjibo_runtime_usage_shadow_source_reader',raw_relation.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') OR pg_catalog.has_any_column_privilege('openjibo_runtime_usage_shadow_source_reader',raw_relation.oid,'SELECT,INSERT,UPDATE,REFERENCES') THEN RAISE EXCEPTION 'shadow-source login or capability retains raw relation privilege'; END IF;
    IF raw_relation.oid IN ('{{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxMessages'::regclass,'{{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxDelivery'::regclass) THEN
      IF NOT pg_catalog.has_table_privilege('openjibo_runtime_usage_shadow_source_owner',raw_relation.oid,'SELECT') OR pg_catalog.has_table_privilege('openjibo_runtime_usage_shadow_source_owner',raw_relation.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') OR pg_catalog.has_any_column_privilege('openjibo_runtime_usage_shadow_source_owner',raw_relation.oid,'INSERT,UPDATE,REFERENCES') THEN RAISE EXCEPTION 'shadow-source owner raw relation privilege is unsafe'; END IF;
    ELSIF pg_catalog.has_table_privilege('openjibo_runtime_usage_shadow_source_owner',raw_relation.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') OR pg_catalog.has_any_column_privilege('openjibo_runtime_usage_shadow_source_owner',raw_relation.oid,'SELECT,INSERT,UPDATE,REFERENCES') THEN RAISE EXCEPTION 'shadow-source owner retains extra raw relation privilege'; END IF;
  END LOOP;
  IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='{{STATE_SCHEMA_LITERAL}}' AND (pg_catalog.has_function_privilege('openjibo_runtime_usage_shadow_source',p.oid,'EXECUTE') OR pg_catalog.has_function_privilege('openjibo_runtime_usage_shadow_source_reader',p.oid,'EXECUTE') OR pg_catalog.has_function_privilege('openjibo_runtime_usage_shadow_source_owner',p.oid,'EXECUTE'))) THEN RAISE EXCEPTION 'shadow-source raw function privilege leaked'; END IF;
  IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(p.proacl,pg_catalog.acldefault('f',p.proowner))) a WHERE n.nspname='runtime_usage_shadow_source' AND p.oid IS DISTINCT FROM to_regprocedure('runtime_usage_shadow_source.describe_runtime_usage_stream(uuid,date,text)') AND p.oid IS DISTINCT FROM to_regprocedure('runtime_usage_shadow_source.read_runtime_usage_stream_page(uuid,date,text,bigint,bigint,integer)') AND (a.grantee=0 OR pg_catalog.has_function_privilege('openjibo_runtime_usage_shadow_source',p.oid,'EXECUTE') OR pg_catalog.has_function_privilege('openjibo_runtime_usage_shadow_source_reader',p.oid,'EXECUTE') OR pg_catalog.has_function_privilege('openjibo_runtime_usage_shadow_source_owner',p.oid,'EXECUTE'))) THEN RAISE EXCEPTION 'shadow-source extra wrapper function is executable'; END IF;
END $verify$;
