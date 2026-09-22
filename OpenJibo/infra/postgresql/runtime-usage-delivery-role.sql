-- Open Jibo runtime-usage source delivery deployment artifact.
--
-- This file is deliberately outside the migration directory.  The migration
-- creates the state tables/functions and keeps them owner-only; this artifact
-- is the separately reviewed activation boundary.  The provisioner replaces
-- the two quoted placeholders before executing the artifact:
--   {{STATE_SCHEMA_IDENTIFIER}} state schema identifier (for example, public)
--   {{STATE_SCHEMA_LITERAL}}    quoted state schema name for privilege checks
--   {{SOURCE_LOGIN_IDENTIFIER}} quoted physical login role identifier
--   {{SOURCE_LOGIN_LITERAL}} exact, already-created physical login role name
-- The only supported source login is openjibo_runtime_usage.
--
-- The source login is never created, altered, passworded, or given any direct
-- object privilege here.  The owner/capability roles are fixed names so an
-- operator cannot accidentally cross this boundary with another principal.

DO $preflight$
DECLARE
    v_login_oid oid;
    v_owner_oid oid;
    v_capability_oid oid;
    v_schema_owner oid;
    v_admin_oid oid;
BEGIN
    IF '{{STATE_SCHEMA_LITERAL}}' IN
       ('runtime_usage_delivery', 'pg_catalog', 'information_schema', 'pg_temp') THEN
        RAISE EXCEPTION 'configured state schema crosses a reserved delivery boundary';
    END IF;
    IF '{{SOURCE_LOGIN_LITERAL}}' = '' OR
       '{{SOURCE_LOGIN_LITERAL}}' !~ '^[a-zA-Z_][a-zA-Z0-9_$-]{0,62}$' THEN
        RAISE EXCEPTION 'runtime usage source login role name is invalid';
    END IF;
    IF '{{SOURCE_LOGIN_LITERAL}}' <> 'openjibo_runtime_usage' THEN
        RAISE EXCEPTION 'runtime usage source login role must be openjibo_runtime_usage';
    END IF;

    SELECT oid INTO v_login_oid FROM pg_catalog.pg_roles
    WHERE rolname = '{{SOURCE_LOGIN_LITERAL}}';
    IF v_login_oid IS NULL THEN
        RAISE EXCEPTION 'runtime usage source login role must be pre-created';
    END IF;
    IF EXISTS (
        SELECT 1 FROM pg_catalog.pg_roles
        WHERE oid = v_login_oid
          AND (NOT rolcanlogin OR NOT rolinherit OR rolsuper OR rolcreatedb OR
               rolcreaterole OR rolreplication OR rolbypassrls)
    ) THEN
        RAISE EXCEPTION 'runtime usage source login role attributes are unsafe';
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_catalog.pg_roles
        WHERE oid = v_login_oid AND rolconnlimit <> 3
    ) THEN
        RAISE EXCEPTION 'runtime usage source login connection limit must be 3';
    END IF;

    SELECT oid INTO v_owner_oid FROM pg_catalog.pg_roles
    WHERE rolname = 'openjibo_runtime_usage_delivery_owner';
    SELECT oid INTO v_capability_oid FROM pg_catalog.pg_roles
    WHERE rolname = 'openjibo_runtime_usage_delivery';
    IF v_owner_oid IS NOT NULL AND EXISTS (
        SELECT 1 FROM pg_catalog.pg_roles
        WHERE oid = v_owner_oid
          AND (rolcanlogin OR rolinherit OR rolsuper OR rolcreatedb OR
               rolcreaterole OR rolreplication OR rolbypassrls)
    ) THEN
        RAISE EXCEPTION 'runtime usage delivery owner role attributes are unsafe';
    END IF;
    IF EXISTS (
        SELECT 1 FROM pg_catalog.pg_roles
        WHERE oid = v_capability_oid
          AND (rolcanlogin OR NOT rolinherit OR rolsuper OR rolcreatedb OR
               rolcreaterole OR rolreplication OR rolbypassrls)
    ) THEN
        RAISE EXCEPTION 'runtime usage delivery capability role attributes are unsafe';
    END IF;

    SELECT n.nspowner INTO v_schema_owner
    FROM pg_catalog.pg_namespace AS n
    WHERE n.nspname = 'runtime_usage_delivery';
    SELECT oid INTO v_admin_oid FROM pg_catalog.pg_roles
    WHERE rolname = CURRENT_USER;
    IF v_schema_owner IS NOT NULL AND v_schema_owner <> v_admin_oid THEN
        RAISE EXCEPTION 'runtime_usage_delivery schema must remain deployment-administrator owned';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM pg_catalog.pg_namespace AS n
        CROSS JOIN LATERAL pg_catalog.aclexplode(
            COALESCE(n.nspacl, pg_catalog.acldefault('n'::"char", n.nspowner))) AS acl
        WHERE n.nspname = 'runtime_usage_delivery'
          AND acl.grantee = 0
    ) THEN
        RAISE EXCEPTION 'runtime_usage_delivery schema has unsafe PUBLIC ACL';
    END IF;

    -- A pre-existing source identity may only be a member of the one
    -- capability role.  Any crossover is operator-visible and fail-closed.
    IF EXISTS (
        SELECT 1
        FROM pg_catalog.pg_auth_members AS m
        JOIN pg_catalog.pg_roles AS member ON member.oid = m.member
        JOIN pg_catalog.pg_roles AS parent ON parent.oid = m.roleid
        WHERE member.oid = v_login_oid
          AND parent.rolname <> 'openjibo_runtime_usage_delivery'
    ) THEN
        RAISE EXCEPTION 'runtime usage source login has an unexpected role membership';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM pg_catalog.pg_auth_members AS m
        JOIN pg_catalog.pg_roles AS member ON member.oid = m.member
        WHERE member.oid IN (v_owner_oid, v_capability_oid)
    ) THEN
        RAISE EXCEPTION 'runtime usage supporting roles inherit an unexpected role';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM pg_catalog.pg_auth_members AS m
        JOIN pg_catalog.pg_roles AS member ON member.oid = m.member
        WHERE m.roleid = v_capability_oid AND member.oid <> v_login_oid
    ) THEN
        RAISE EXCEPTION 'runtime usage capability role has an unexpected member';
    END IF;
    IF v_owner_oid IS NOT NULL AND EXISTS (
        SELECT 1
        FROM pg_catalog.pg_auth_members AS m
        JOIN pg_catalog.pg_roles AS member ON member.oid = m.member
        WHERE m.roleid = v_owner_oid AND member.rolname <> CURRENT_USER
    ) THEN
        RAISE EXCEPTION 'runtime usage owner role has an unexpected member';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM pg_catalog.pg_namespace AS n
        CROSS JOIN LATERAL pg_catalog.aclexplode(
            COALESCE(n.nspacl, pg_catalog.acldefault('n'::"char", n.nspowner))) AS acl
        WHERE lower(n.nspname) = lower('{{STATE_SCHEMA_LITERAL}}')
          AND acl.grantee = 0
          AND acl.privilege_type = 'CREATE'
    ) THEN
        RAISE EXCEPTION 'configured state schema grants CREATE to PUBLIC';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM pg_catalog.pg_class AS c
        JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
        CROSS JOIN LATERAL pg_catalog.aclexplode(
            COALESCE(c.relacl, pg_catalog.acldefault(
                CASE WHEN c.relkind = 'S' THEN 'S'::"char" ELSE 'r'::"char" END,
                c.relowner))) AS acl
        WHERE lower(n.nspname) = lower('{{STATE_SCHEMA_LITERAL}}')
          AND lower(c.relname) LIKE 'runtimeusage%'
          AND c.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')
          AND acl.grantee IN (v_login_oid, v_capability_oid)
    ) THEN
        RAISE EXCEPTION 'runtime usage source login has direct table privileges';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM pg_catalog.pg_class AS c
        JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
        CROSS JOIN LATERAL pg_catalog.aclexplode(
            COALESCE(c.relacl, pg_catalog.acldefault(
                CASE WHEN c.relkind = 'S' THEN 'S'::"char" ELSE 'r'::"char" END,
                c.relowner))) AS acl
        WHERE lower(n.nspname) = lower('{{STATE_SCHEMA_LITERAL}}')
          AND lower(c.relname) LIKE 'runtimeusage%'
          AND c.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')
          AND acl.grantee = 0
    ) THEN
        RAISE EXCEPTION 'runtime usage state tables grant PUBLIC privileges';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM pg_catalog.pg_proc AS p
        JOIN pg_catalog.pg_namespace AS n ON n.oid = p.pronamespace
        CROSS JOIN LATERAL pg_catalog.aclexplode(
            COALESCE(p.proacl, pg_catalog.acldefault('f'::"char", p.proowner))) AS acl
        WHERE lower(n.nspname) = lower('{{STATE_SCHEMA_LITERAL}}')
          AND lower(p.proname) LIKE '%runtimeusage%'
          AND acl.grantee IN (v_login_oid, v_capability_oid)
    ) THEN
        RAISE EXCEPTION 'runtime usage source login has direct raw-function privileges';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM pg_catalog.pg_proc AS p
        JOIN pg_catalog.pg_namespace AS n ON n.oid = p.pronamespace
        CROSS JOIN LATERAL pg_catalog.aclexplode(
            COALESCE(p.proacl, pg_catalog.acldefault('f'::"char", p.proowner))) AS acl
        WHERE lower(n.nspname) = lower('{{STATE_SCHEMA_LITERAL}}')
          AND lower(p.proname) LIKE '%runtimeusage%'
          AND acl.grantee = 0
    ) THEN
        RAISE EXCEPTION 'runtime usage state functions grant PUBLIC privileges';
    END IF;
END
$preflight$;

-- The supporting roles are intentionally NOLOGIN and non-inheriting.  They
-- are created only after the preflight proves the physical login exists.
DO $roles$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                   WHERE rolname = 'openjibo_runtime_usage_delivery_owner') THEN
        CREATE ROLE openjibo_runtime_usage_delivery_owner NOLOGIN NOINHERIT NOSUPERUSER
            NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                   WHERE rolname = 'openjibo_runtime_usage_delivery') THEN
        CREATE ROLE openjibo_runtime_usage_delivery NOLOGIN INHERIT NOSUPERUSER
            NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
    END IF;
END
$roles$;

ALTER ROLE openjibo_runtime_usage_delivery_owner NOLOGIN NOINHERIT NOSUPERUSER
    NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
ALTER ROLE openjibo_runtime_usage_delivery NOLOGIN INHERIT NOSUPERUSER
    NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;

-- The schema is deployment-owned and contains wrappers only.  It is not the
-- configured state schema and never receives table or raw-function grants.
DO $schema$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_catalog.pg_namespace
        WHERE nspname = 'runtime_usage_delivery'
    ) THEN
        EXECUTE 'CREATE SCHEMA runtime_usage_delivery';
    END IF;
END
$schema$;

GRANT USAGE, CREATE ON SCHEMA runtime_usage_delivery
    TO openjibo_runtime_usage_delivery_owner;
GRANT USAGE ON SCHEMA {{STATE_SCHEMA_IDENTIFIER}}
    TO openjibo_runtime_usage_delivery_owner;
GRANT USAGE ON SCHEMA runtime_usage_delivery
    TO openjibo_runtime_usage_delivery;

-- The deployment administrator temporarily assumes the dedicated owner so
-- function creation/ownership never leaves the migration administrator as a
-- wrapper owner.  This membership is administrative and cannot be inherited
-- by the source login.
GRANT openjibo_runtime_usage_delivery_owner TO CURRENT_USER
    WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;

-- Underlying functions run as this owner through the wrappers.  No attempt-cap
-- recovery function is present in this list by design.
GRANT SELECT ON TABLE
    {{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxMessages
    TO openjibo_runtime_usage_delivery_owner;
GRANT SELECT, UPDATE ON TABLE
    {{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxDelivery
    TO openjibo_runtime_usage_delivery_owner;
GRANT SELECT, INSERT ON TABLE
    {{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxDeferrals
    TO openjibo_runtime_usage_delivery_owner;
GRANT EXECUTE ON FUNCTION
    {{STATE_SCHEMA_IDENTIFIER}}.ClaimRuntimeUsageOutbox(TEXT, INTEGER, INTEGER)
    TO openjibo_runtime_usage_delivery_owner;
GRANT EXECUTE ON FUNCTION
    {{STATE_SCHEMA_IDENTIFIER}}.DeferRuntimeUsageOutbox(UUID, TEXT, UUID, TIMESTAMPTZ, TEXT)
    TO openjibo_runtime_usage_delivery_owner;
GRANT EXECUTE ON FUNCTION
    {{STATE_SCHEMA_IDENTIFIER}}.AcknowledgeRuntimeUsageOutbox(UUID, TEXT, BYTEA)
    TO openjibo_runtime_usage_delivery_owner;
GRANT EXECUTE ON FUNCTION
    {{STATE_SCHEMA_IDENTIFIER}}.QuarantineRuntimeUsageOutbox(UUID, TEXT, TEXT)
    TO openjibo_runtime_usage_delivery_owner;

SET LOCAL ROLE openjibo_runtime_usage_delivery_owner;

CREATE OR REPLACE FUNCTION runtime_usage_delivery.ClaimRuntimeUsageOutbox(
    p_lease_owner TEXT,
    p_lease_seconds INTEGER DEFAULT 300,
    p_batch_size INTEGER DEFAULT 1)
RETURNS TABLE
(
    MessageId UUID, ManagedRobotId UUID, UsageDate DATE,
    ServiceEnvironment TEXT, SourceSequence BIGINT, AccumulatorRevision BIGINT,
    FormatVersion SMALLINT, SourceSchemaVersion TEXT, SourceRevision TEXT,
    SuccessfulTurns BIGINT, FailedTurns BIGINT, HttpRequests BIGINT,
    HttpRequestBytes BIGINT, HttpResponseBytes BIGINT,
    WebSocketInboundMessages BIGINT, WebSocketOutboundMessages BIGINT,
    WebSocketInboundBytes BIGINT, WebSocketOutboundBytes BIGINT,
    AudioInputBytes BIGINT, FirstEventUtc TIMESTAMPTZ, LastEventUtc TIMESTAMPTZ,
    IsIncomplete BOOLEAN, FirstIncompleteUtc TIMESTAMPTZ,
    IncompleteReasonCode TEXT, IdempotencyKey TEXT,
    DeliveryAttemptCount INTEGER, LeaseExpiresUtc TIMESTAMPTZ)
LANGUAGE sql SECURITY DEFINER
SET search_path = pg_catalog, {{STATE_SCHEMA_IDENTIFIER}}, pg_temp
AS $$ SELECT * FROM {{STATE_SCHEMA_IDENTIFIER}}.ClaimRuntimeUsageOutbox($1, $2, $3) $$;

CREATE OR REPLACE FUNCTION runtime_usage_delivery.DeferRuntimeUsageOutbox(
    p_message_id UUID, p_lease_owner TEXT, p_operation_id UUID,
    p_not_before_utc TIMESTAMPTZ, p_failure_category TEXT)
RETURNS TABLE (DeferredMessageId UUID, DeferredUtc TIMESTAMPTZ, WasReplay BOOLEAN)
LANGUAGE sql SECURITY DEFINER
SET search_path = pg_catalog, {{STATE_SCHEMA_IDENTIFIER}}, pg_temp
AS $$ SELECT * FROM {{STATE_SCHEMA_IDENTIFIER}}.DeferRuntimeUsageOutbox($1, $2, $3, $4, $5) $$;

CREATE OR REPLACE FUNCTION runtime_usage_delivery.AcknowledgeRuntimeUsageOutbox(
    p_message_id UUID, p_lease_owner TEXT, p_receipt_hash BYTEA)
RETURNS TABLE (AcknowledgedMessageId UUID, AcknowledgedUtc TIMESTAMPTZ, WasReplay BOOLEAN)
LANGUAGE sql SECURITY DEFINER
SET search_path = pg_catalog, {{STATE_SCHEMA_IDENTIFIER}}, pg_temp
AS $$ SELECT * FROM {{STATE_SCHEMA_IDENTIFIER}}.AcknowledgeRuntimeUsageOutbox($1, $2, $3) $$;

CREATE OR REPLACE FUNCTION runtime_usage_delivery.QuarantineRuntimeUsageOutbox(
    p_message_id UUID, p_lease_owner TEXT, p_quarantine_category TEXT)
RETURNS TABLE (QuarantinedMessageId UUID, QuarantinedUtc TIMESTAMPTZ, WasReplay BOOLEAN)
LANGUAGE sql SECURITY DEFINER
SET search_path = pg_catalog, {{STATE_SCHEMA_IDENTIFIER}}, pg_temp
AS $$ SELECT * FROM {{STATE_SCHEMA_IDENTIFIER}}.QuarantineRuntimeUsageOutbox($1, $2, $3) $$;

ALTER FUNCTION runtime_usage_delivery.ClaimRuntimeUsageOutbox(TEXT, INTEGER, INTEGER)
    OWNER TO openjibo_runtime_usage_delivery_owner;
ALTER FUNCTION runtime_usage_delivery.DeferRuntimeUsageOutbox(UUID, TEXT, UUID, TIMESTAMPTZ, TEXT)
    OWNER TO openjibo_runtime_usage_delivery_owner;
ALTER FUNCTION runtime_usage_delivery.AcknowledgeRuntimeUsageOutbox(UUID, TEXT, BYTEA)
    OWNER TO openjibo_runtime_usage_delivery_owner;
ALTER FUNCTION runtime_usage_delivery.QuarantineRuntimeUsageOutbox(UUID, TEXT, TEXT)
    OWNER TO openjibo_runtime_usage_delivery_owner;

RESET ROLE;

REVOKE ALL ON SCHEMA runtime_usage_delivery FROM PUBLIC;
REVOKE ALL ON FUNCTION runtime_usage_delivery.ClaimRuntimeUsageOutbox(TEXT, INTEGER, INTEGER) FROM PUBLIC;
REVOKE ALL ON FUNCTION runtime_usage_delivery.DeferRuntimeUsageOutbox(UUID, TEXT, UUID, TIMESTAMPTZ, TEXT) FROM PUBLIC;
REVOKE ALL ON FUNCTION runtime_usage_delivery.AcknowledgeRuntimeUsageOutbox(UUID, TEXT, BYTEA) FROM PUBLIC;
REVOKE ALL ON FUNCTION runtime_usage_delivery.QuarantineRuntimeUsageOutbox(UUID, TEXT, TEXT) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION runtime_usage_delivery.ClaimRuntimeUsageOutbox(TEXT, INTEGER, INTEGER)
    TO openjibo_runtime_usage_delivery;
GRANT EXECUTE ON FUNCTION runtime_usage_delivery.DeferRuntimeUsageOutbox(UUID, TEXT, UUID, TIMESTAMPTZ, TEXT)
    TO openjibo_runtime_usage_delivery;
GRANT EXECUTE ON FUNCTION runtime_usage_delivery.AcknowledgeRuntimeUsageOutbox(UUID, TEXT, BYTEA)
    TO openjibo_runtime_usage_delivery;
GRANT EXECUTE ON FUNCTION runtime_usage_delivery.QuarantineRuntimeUsageOutbox(UUID, TEXT, TEXT)
    TO openjibo_runtime_usage_delivery;

-- The login must be a member of exactly the capability role.  Do not grant it
-- owner, raw function, table, view, or schema CREATE access.
GRANT openjibo_runtime_usage_delivery TO {{SOURCE_LOGIN_IDENTIFIER}}
    WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;
REVOKE ALL ON TABLE
    {{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxMessages,
    {{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxDelivery,
    {{STATE_SCHEMA_IDENTIFIER}}.RuntimeUsageOutboxDeferrals
    FROM PUBLIC, {{SOURCE_LOGIN_IDENTIFIER}}, openjibo_runtime_usage_delivery;
REVOKE ALL ON FUNCTION
    {{STATE_SCHEMA_IDENTIFIER}}.ClaimRuntimeUsageOutbox(TEXT, INTEGER, INTEGER),
    {{STATE_SCHEMA_IDENTIFIER}}.DeferRuntimeUsageOutbox(UUID, TEXT, UUID, TIMESTAMPTZ, TEXT),
    {{STATE_SCHEMA_IDENTIFIER}}.AcknowledgeRuntimeUsageOutbox(UUID, TEXT, BYTEA),
    {{STATE_SCHEMA_IDENTIFIER}}.QuarantineRuntimeUsageOutbox(UUID, TEXT, TEXT)
    FROM PUBLIC, {{SOURCE_LOGIN_IDENTIFIER}}, openjibo_runtime_usage_delivery;

REVOKE CREATE ON SCHEMA runtime_usage_delivery FROM {{SOURCE_LOGIN_IDENTIFIER}};
REVOKE CREATE ON SCHEMA runtime_usage_delivery FROM openjibo_runtime_usage_delivery_owner;

DO $verify$
DECLARE
    v_login_oid oid;
BEGIN
    SELECT oid INTO v_login_oid FROM pg_catalog.pg_roles
    WHERE rolname = '{{SOURCE_LOGIN_LITERAL}}';
    IF EXISTS (
        SELECT 1 FROM pg_catalog.pg_auth_members AS m
        JOIN pg_catalog.pg_roles AS member ON member.oid = m.member
        JOIN pg_catalog.pg_roles AS parent ON parent.oid = m.roleid
        WHERE member.oid = v_login_oid
          AND parent.rolname <> 'openjibo_runtime_usage_delivery'
    ) THEN
        RAISE EXCEPTION 'runtime usage source login has an unexpected role membership';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_catalog.pg_auth_members AS m
        JOIN pg_catalog.pg_roles AS member ON member.oid = m.member
        JOIN pg_catalog.pg_roles AS parent ON parent.oid = m.roleid
        WHERE member.oid = v_login_oid
          AND parent.rolname = 'openjibo_runtime_usage_delivery'
          AND m.inherit_option AND NOT m.set_option AND NOT m.admin_option
    ) THEN
        RAISE EXCEPTION 'runtime usage source login capability membership is invalid';
    END IF;
    IF pg_catalog.has_schema_privilege('{{SOURCE_LOGIN_LITERAL}}', '{{STATE_SCHEMA_LITERAL}}', 'CREATE') OR
       pg_catalog.has_schema_privilege('{{SOURCE_LOGIN_LITERAL}}', 'runtime_usage_delivery', 'CREATE') OR
       pg_catalog.has_table_privilege('{{SOURCE_LOGIN_LITERAL}}', '{{STATE_SCHEMA_LITERAL}}.RuntimeUsageOutboxMessages', 'SELECT') OR
       pg_catalog.has_table_privilege('{{SOURCE_LOGIN_LITERAL}}', '{{STATE_SCHEMA_LITERAL}}.RuntimeUsageOutboxDelivery', 'UPDATE') OR
       pg_catalog.has_function_privilege('{{SOURCE_LOGIN_LITERAL}}', '{{STATE_SCHEMA_LITERAL}}.ClaimRuntimeUsageOutbox(text,integer,integer)', 'EXECUTE') THEN
        RAISE EXCEPTION 'runtime usage source login retains a direct object privilege';
    END IF;
    IF pg_catalog.has_schema_privilege(
           'openjibo_runtime_usage_delivery_owner',
           'runtime_usage_delivery',
           'CREATE') THEN
        RAISE EXCEPTION 'runtime usage delivery owner retains schema CREATE privilege';
    END IF;
END
$verify$;
