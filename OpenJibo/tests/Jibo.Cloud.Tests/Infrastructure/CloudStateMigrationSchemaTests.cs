namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class CloudStateMigrationSchemaTests
{
    [Fact]
    public void NormalizedPeople_UsesAccountLoopPersonCompositeIdentity()
    {
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
            "004_normalize_cloud_state.state.sql"));

        Assert.Contains("PRIMARY KEY (AccountId, LoopId, PersonId)", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("PersonId TEXT NOT NULL PRIMARY KEY", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudAuthTokens_AllowObservedHardwareBeforeInventoryRegistration()
    {
        var forwardMigration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
            "006_allow_unlinked_cloud_auth_tokens.state.sql"));

        Assert.Contains(
            "DROP CONSTRAINT IF EXISTS cloudauthtokens_deviceid_fkey",
            forwardMigration,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RobotIdentitySuggestions_AreDurableAndCaseInsensitive()
    {
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
            "009_create_robot_identity_suggestions.state.sql"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS RobotIdentitySuggestions", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOWER(ObservedDeviceId), LOWER(ProposedRobotId)", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DismissedUtc IS NULL", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UserDevices_EnforceOneCurrentOwnerPerDevice()
    {
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
            "008_create_user_devices.state.sql"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS UserDevices", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS UX_UserDevices_Device", migration,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Devices_IndexVerifiedSerialNumbersCaseInsensitively()
    {
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
            "010_index_verified_serial_number.state.sql"));

        Assert.Contains("CREATE INDEX IF NOT EXISTS IX_Devices_VerifiedSerialNumber_CI", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON Devices (LOWER(VerifiedSerialNumber))", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE VerifiedSerialNumber IS NOT NULL", migration,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeUsageOutbox_IsPrivacyBoundImmutableAndDeliverySeparated()
    {
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
            "011_create_runtime_usage_outbox.state.sql"));

        Assert.Contains("RuntimeUsageRobotBindings", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SourceSubjectHmac BYTEA", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OCTET_LENGTH(SourceSubjectHmac) = 32", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RuntimeUsageDailyAccumulators", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsIncomplete BOOLEAN NOT NULL DEFAULT FALSE", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RuntimeUsageAppliedEvents", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RecordRuntimeUsageEvent", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ScheduleRuntimeUsageSnapshot", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("openjibo-runtime-usage.v1", migration, StringComparison.Ordinal);
        Assert.Contains("RuntimeUsageOutboxMessages", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WebSocketInboundBytes BIGINT", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CanonicalPayload", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RuntimeUsageOutboxDelivery", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEFORE UPDATE OR DELETE", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OLD.IsIncomplete AND NOT NEW.IsIncomplete", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bindings are append-only and may only be revoked once", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grants no application, binding-administrator, or collector role", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE EXECUTE ON FUNCTION", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeviceId", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Serial", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Transcript", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeUsageDeliveryBoundary_IsOwnerOnlyAndHeadOfLineSafe()
    {
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
            "012_runtime_usage_delivery_boundary.state.sql"));

        Assert.Contains("CREATE OR REPLACE FUNCTION ClaimRuntimeUsageOutbox", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE OR REPLACE FUNCTION AcknowledgeRuntimeUsageOutbox", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE OR REPLACE FUNCTION QuarantineRuntimeUsageOutbox", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FOR UPDATE OF delivery SKIP LOCKED", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("earlier_delivery.DeliveryState <> 'acknowledged'", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LeaseExpiresUtc <= v_now", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p_message_id IS NULL", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p_receipt_hash IS NULL", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p_quarantine_category IS NULL", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE ALL ON FUNCTION ClaimRuntimeUsageOutbox", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE ALL ON FUNCTION AcknowledgeRuntimeUsageOutbox", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE ALL ON FUNCTION QuarantineRuntimeUsageOutbox", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT EXECUTE", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECURITY DEFINER", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeviceId", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Transcript", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AudioContent", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SigV4ReplayObservation_IsShortLivedOpaqueAndOwnerOnly()
    {
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
            "013_create_sigv4_replay_observations.state.sql"));

        Assert.Contains("AwsSigV4ReplayObservations", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OCTET_LENGTH(ReplayDigest) = 32", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INTERVAL '15 minutes'", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON CONFLICT (ReplayDigest) DO UPDATE", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT 256", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE ALL ON FUNCTION ObserveAwsSigV4Replay", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT EXECUTE", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccessKeyId", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeviceId", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SigV4ReplayObservation_HardenedFunctionUsesRestrictedSearchPath()
    {
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
            "014_harden_sigv4_replay_observer.state.sql"));

        Assert.Contains("SECURITY DEFINER", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET search_path = pg_catalog, pg_temp", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public.AwsSigV4ReplayObservations", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE ALL ON FUNCTION public.ObserveAwsSigV4Replay", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT EXECUTE", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeUsageDeliveryDefer_IsOwnerOnlyBoundedAndUnwired()
    {
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
            "015_runtime_usage_defer_boundary.state.sql"));

        Assert.Contains("CREATE OR REPLACE FUNCTION DeferRuntimeUsageOutbox", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS RuntimeUsageOutboxDeferrals", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p_operation_id UUID", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p_not_before_utc > v_now + INTERVAL '24 hours'", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LeaseExpiresUtc > clock_timestamp()", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RejectRuntimeUsageOutboxDeferralMutation", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pg_advisory_xact_lock", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WasReplay", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE ALL ON FUNCTION DeferRuntimeUsageOutbox", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE ALL ON RuntimeUsageOutboxDeferrals FROM PUBLIC", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT EXECUTE", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECURITY DEFINER", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RecordRuntimeUsageEvent", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeUsageAttemptCapRecovery_IsImmutableOwnerOnlyFixedAndUnwired()
    {
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
            "016_runtime_usage_attempt_cap_recovery.state.sql"));

        Assert.Contains("RuntimeUsageOutboxAttemptCapRecoveryReceipts", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RecoveryOperationId UUID", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RecoveryOperationId <> '00000000-0000-0000-0000-000000000000'", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EvidenceDigest BYTEA", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OCTET_LENGTH(EvidenceDigest) = 32", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PriorAttemptCount INTEGER NOT NULL CHECK (PriorAttemptCount = 100000)", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PriorNotBeforeUtc", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PriorLeaseExpiresUtc", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SessionUser", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CurrentUser", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RecoveredUtc", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ActionCode TEXT NOT NULL CHECK (ActionCode = 'quarantine')", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QuarantineCategory TEXT NOT NULL CHECK (QuarantineCategory = 'attempt-cap-exhausted')",
            migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE OR REPLACE FUNCTION RecoverRuntimeUsageOutboxAtAttemptCap", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p_message_id UUID", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p_recovery_operation_id UUID", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p_evidence_digest BYTEA", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEFORE UPDATE OR DELETE", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pg_advisory_xact_lock", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeliveryState = 'quarantined'", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AttemptCount <> 100000", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LeaseExpiresUtc > v_now", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotBeforeUtc > v_now", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE ALL ON FUNCTION RecoverRuntimeUsageOutboxAtAttemptCap(UUID, UUID, BYTEA)",
            migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE ALL ON RuntimeUsageOutboxAttemptCapRecoveryReceipts FROM PUBLIC", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GET DIAGNOSTICS v_updated_count = ROW_COUNT", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT EXECUTE", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECURITY DEFINER", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RecordRuntimeUsageEvent", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ClaimRuntimeUsageOutbox", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CustomerId", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SerialNumber", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Transcript", migration, StringComparison.OrdinalIgnoreCase);
    }

}
