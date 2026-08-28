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
}
