using System.Security.Cryptography;
using Jibo.Cloud.Infrastructure.Persistence;
using Npgsql;

namespace Jibo.Cloud.Tests.Infrastructure;

[Collection(PostgreSqlPrivilegedIntegrationCollection.Name)]
public sealed class PostgreSqlRuntimeUsageDeliveryRolePrivilegedIntegrationTests
{
    [PostgreSqlPrivilegedIntegrationFact]
    [Trait("Category", "PostgreSqlPrivilegedIntegration")]
    public async Task Provisioner_IsIdempotentAndKeepsSourceBoundaryNarrow()
    {
        var administratorConnectionString =
            Environment.GetEnvironmentVariable("OPENJIBO_TEST_POSTGRES_PRIVILEGED_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set OPENJIBO_TEST_POSTGRES_PRIVILEGED_CONNECTION_STRING.");
        var schema = $"openjibo_runtime_delivery_test_{Guid.NewGuid():N}";
        var sourcePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var createdSource = false;
        var ownerExistedBefore = false;
        var capabilityExistedBefore = false;

        try
        {
            await ExecuteAsync(administratorConnectionString,
                $"CREATE SCHEMA {QuoteIdentifier(schema)}");
            ownerExistedBefore = await RoleWasCreatedAsync(administratorConnectionString,
                PostgreSqlRuntimeUsageDeliveryRoleProvisioner.OwnerRole);
            capabilityExistedBefore = await RoleWasCreatedAsync(administratorConnectionString,
                PostgreSqlRuntimeUsageRoleProvisionerCapability());
            await EnsureRoleAsync(administratorConnectionString,
                PostgreSqlRuntimeUsageDeliveryRoleProvisioner.LoginRole,
                $"LOGIN INHERIT CONNECTION LIMIT 3 PASSWORD {QuoteLiteral(sourcePassword)}",
                value => createdSource = value);

            foreach (var migration in new[]
                     {
                         "011_create_runtime_usage_outbox.state.sql",
                         "012_runtime_usage_delivery_boundary.state.sql",
                         "015_runtime_usage_defer_boundary.state.sql",
                         "016_runtime_usage_attempt_cap_recovery.state.sql"
                     })
            {
                await ExecuteAsync(
                    administratorConnectionString,
                    await File.ReadAllTextAsync(Path.Combine(
                        AppContext.BaseDirectory, "Migrations", "PostgreSql", migration)),
                    schema);
            }

            await PostgreSqlRuntimeUsageDeliveryRoleProvisioner.ProvisionAsync(
                administratorConnectionString, schema,
                PostgreSqlRuntimeUsageRoleProvisionerLogin());
            await PostgreSqlRuntimeUsageDeliveryRoleProvisioner.ProvisionAsync(
                administratorConnectionString, schema,
                PostgreSqlRuntimeUsageRoleProvisionerLogin());

            var ownerAndPath = await ReadScalarAsync<string>(administratorConnectionString, """
                SELECT pg_catalog.pg_get_userbyid(p.proowner) || '|' || p.proconfig::text
                FROM pg_catalog.pg_proc AS p
                JOIN pg_catalog.pg_namespace AS n ON n.oid = p.pronamespace
                WHERE n.nspname = 'runtime_usage_delivery'
                  AND lower(p.proname) = 'claimruntimeusageoutbox'
                """);
            Assert.StartsWith(PostgreSqlRuntimeUsageDeliveryRoleProvisioner.OwnerRole + "|", ownerAndPath,
                StringComparison.Ordinal);
            Assert.Contains("search_path=pg_catalog", ownerAndPath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("pg_temp", ownerAndPath, StringComparison.OrdinalIgnoreCase);

            var sourceConnection = new NpgsqlConnectionStringBuilder(administratorConnectionString)
            {
                Username = PostgreSqlRuntimeUsageDeliveryRoleProvisioner.LoginRole,
                Password = sourcePassword,
                Pooling = false
            }.ConnectionString;
            Assert.Equal(0, await ReadScalarAsync<long>(sourceConnection, """
                SELECT COUNT(*)
                FROM runtime_usage_delivery.ClaimRuntimeUsageOutbox('integration-collector', 300, 1)
                """));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,
                await TryExecuteAsync(sourceConnection,
                    $"SELECT COUNT(*) FROM {QuoteIdentifier(schema)}.RuntimeUsageOutboxMessages"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,
                await TryExecuteAsync(sourceConnection,
                    $"SELECT * FROM {QuoteIdentifier(schema)}.ClaimRuntimeUsageOutbox('collector', 300, 1)"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,
                await TryExecuteAsync(sourceConnection,
                    $"SET ROLE {QuoteIdentifier(PostgreSqlRuntimeUsageDeliveryRoleProvisioner.CapabilityRole)}"));

            Assert.False(await ReadScalarAsync<bool>(administratorConnectionString, $"""
                SELECT pg_catalog.has_function_privilege(
                    '{PostgreSqlRuntimeUsageDeliveryRoleProvisioner.LoginRole}',
                    '{schema}.RecoverRuntimeUsageOutboxAtAttemptCap(uuid,uuid,bytea)', 'EXECUTE')
                """));
            Assert.False(await ReadScalarAsync<bool>(administratorConnectionString, $"""
                SELECT pg_catalog.has_table_privilege(
                    '{PostgreSqlRuntimeUsageDeliveryRoleProvisioner.LoginRole}',
                    '{schema}.RuntimeUsageRobotBindings', 'SELECT')
                """));
        }
        finally
        {
            await TryExecuteAsync(administratorConnectionString, "RESET ROLE");
            await TryExecuteAsync(administratorConnectionString, "REVOKE openjibo_runtime_usage_delivery_owner FROM CURRENT_USER");
            await TryExecuteAsync(administratorConnectionString, "DROP SCHEMA runtime_usage_delivery CASCADE");
            await TryExecuteAsync(administratorConnectionString, $"DROP SCHEMA {QuoteIdentifier(schema)} CASCADE");
            if (createdSource)
                await TryExecuteAsync(administratorConnectionString,
                    $"DROP ROLE {QuoteIdentifier(PostgreSqlRuntimeUsageDeliveryRoleProvisioner.LoginRole)}");
            if (!capabilityExistedBefore)
                await TryExecuteAsync(administratorConnectionString,
                    $"DROP ROLE {QuoteIdentifier(PostgreSqlRuntimeUsageDeliveryRoleProvisioner.CapabilityRole)}");
            if (!ownerExistedBefore)
                await TryExecuteAsync(administratorConnectionString,
                    $"DROP ROLE {QuoteIdentifier(PostgreSqlRuntimeUsageDeliveryRoleProvisioner.OwnerRole)}");
        }
    }

    private static string PostgreSqlRuntimeUsageRoleProvisionerLogin() =>
        PostgreSqlRuntimeUsageDeliveryRoleProvisioner.LoginRole;

    private static string PostgreSqlRuntimeUsageRoleProvisionerCapability() =>
        PostgreSqlRuntimeUsageDeliveryRoleProvisioner.CapabilityRole;

    private static async Task EnsureRoleAsync(string connectionString, string role, string attributes,
        Action<bool> created)
    {
        var exists = await ReadScalarAsync<bool>(connectionString, $"""
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '{role}')
            """);
        if (exists)
            throw new InvalidOperationException($"Privileged integration role '{role}' already exists.");
        await ExecuteAsync(connectionString, $"CREATE ROLE {QuoteIdentifier(role)} {attributes}");
        created(true);
    }

    private static async Task<bool> RoleWasCreatedAsync(string connectionString, string role) =>
        await ReadScalarAsync<bool>(connectionString, $"""
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '{role}')
            """);

    private static async Task ExecuteAsync(string connectionString, string sql, string? searchPath = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(searchPath)) builder.SearchPath = searchPath;
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ReadScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> TryExecuteAsync(string connectionString, string sql)
    {
        try
        {
            await ExecuteAsync(connectionString, sql);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string QuoteLiteral(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlPrivilegedIntegrationCollection
{
    public const string Name = "PostgreSQL privileged integration";
}
