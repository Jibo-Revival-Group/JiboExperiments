using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PostgreSqlRuntimeUsageDeliveryRoleProvisionerTests
{
    [Fact]
    public async Task ProvisionAsync_RejectsUnsafeStateSchemaBeforeConnecting()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            PostgreSqlRuntimeUsageDeliveryRoleProvisioner.ProvisionAsync(
                "Host=unreachable;Database=openjibo_state;Username=admin",
                "public;DROP SCHEMA public",
                PostgreSqlRuntimeUsageDeliveryRoleProvisioner.LoginRole));

        Assert.Contains("state schema", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionAsync_RejectsSupportingRoleCrossoverBeforeConnecting()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            PostgreSqlRuntimeUsageDeliveryRoleProvisioner.ProvisionAsync(
                "Host=unreachable;Database=openjibo_state;Username=admin",
                "public",
                PostgreSqlRuntimeUsageDeliveryRoleProvisioner.OwnerRole));

        Assert.Contains(PostgreSqlRuntimeUsageDeliveryRoleProvisioner.LoginRole, error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentArtifact_IsSeparateAndOnlyExposesFourDeliveryWrappers()
    {
        var artifact = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Deployment",
            "runtime-usage-delivery-role.sql"));

        Assert.Contains("SECURITY DEFINER", artifact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET search_path = pg_catalog, {{STATE_SCHEMA_IDENTIFIER}}, pg_temp", artifact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime_usage_delivery.ClaimRuntimeUsageOutbox", artifact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime_usage_delivery.DeferRuntimeUsageOutbox", artifact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime_usage_delivery.AcknowledgeRuntimeUsageOutbox", artifact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime_usage_delivery.QuarantineRuntimeUsageOutbox", artifact,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RecoverRuntimeUsageOutboxAtAttemptCap", artifact,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must be pre-created", artifact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("connection limit must be 3", artifact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("openjibo_runtime_usage", artifact, StringComparison.Ordinal);
        Assert.Contains(
            "REVOKE CREATE ON SCHEMA runtime_usage_delivery FROM openjibo_runtime_usage_delivery_owner",
            artifact,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE ROLE {{SOURCE_LOGIN", artifact, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" PASSWORD ", artifact, StringComparison.OrdinalIgnoreCase);
    }
}
