using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PostgreSqlRuntimeUsageShadowSourceRoleProvisionerTests
{
    [Fact]
    public async Task ProvisionerRejectsUnsafeSchemaBeforeConnecting()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.ProvisionAsync(
            "Host=unreachable;Database=openjibo_state;Username=admin", "public;DROP SCHEMA public"));
    }

    [Fact]
    public void ArtifactIsDormantReadOnlyAndBounded()
    {
        var artifact = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Deployment", "runtime-usage-shadow-source-role.sql"));
        Assert.Contains("openjibo_runtime_usage_shadow_source must be pre-created", artifact, StringComparison.Ordinal);
        Assert.Contains("rolconnlimit<>1", artifact, StringComparison.Ordinal);
        Assert.Contains("runtime_usage_shadow_source.describe_runtime_usage_stream", artifact, StringComparison.Ordinal);
        Assert.Contains("runtime_usage_shadow_source.read_runtime_usage_stream_page", artifact, StringComparison.Ordinal);
        Assert.Contains("p_page_size NOT BETWEEN 1 AND 250", artifact, StringComparison.Ordinal);
        Assert.Contains("STABLE SECURITY DEFINER", artifact, StringComparison.Ordinal);
        Assert.Contains("PUBLIC temporary privilege", artifact, StringComparison.Ordinal);
        Assert.Contains("m.roleid=owner_oid AND m.member<>admin_oid", artifact, StringComparison.Ordinal);
        Assert.Contains("shadow-source raw relation ACL is contaminated", artifact, StringComparison.Ordinal);
        Assert.Contains("shadow-source raw function ACL is contaminated", artifact, StringComparison.Ordinal);
        Assert.Contains("shadow-source wrapper schema contains an unexpected function", artifact, StringComparison.Ordinal);
        Assert.Contains("DROP FUNCTION IF EXISTS runtime_usage_shadow_source.describe_runtime_usage_stream", artifact, StringComparison.Ordinal);
        Assert.Contains("openjibo_runtime_usage_shadow_source_owner','runtime_usage_shadow_source','CREATE'", artifact, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimRuntimeUsageOutbox(", artifact, StringComparison.Ordinal);
        Assert.DoesNotContain("AcknowledgeRuntimeUsageOutbox(", artifact, StringComparison.Ordinal);
        Assert.DoesNotContain("QuarantineRuntimeUsageOutbox(", artifact, StringComparison.Ordinal);
        Assert.DoesNotContain(" PASSWORD ", artifact, StringComparison.OrdinalIgnoreCase);
    }
}
