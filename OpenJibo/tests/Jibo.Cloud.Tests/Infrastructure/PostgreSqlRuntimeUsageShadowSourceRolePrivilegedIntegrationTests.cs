using System.Security.Cryptography;
using Jibo.Cloud.Infrastructure.Persistence;
using Npgsql;

namespace Jibo.Cloud.Tests.Infrastructure;

[Collection(PostgreSqlPrivilegedIntegrationCollection.Name)]
public sealed class PostgreSqlRuntimeUsageShadowSourceRolePrivilegedIntegrationTests
{
    [PostgreSqlPrivilegedIntegrationFact]
    [Trait("Category", "PostgreSqlPrivilegedIntegration")]
    public async Task ProvisionerFailsClosedForMembershipRawAclAndWrapperFunctionContamination()
    {
        var administrator = Environment.GetEnvironmentVariable("OPENJIBO_TEST_POSTGRES_PRIVILEGED_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set OPENJIBO_TEST_POSTGRES_PRIVILEGED_CONNECTION_STRING.");
        var schema = $"oj_shadow_contam_{Guid.NewGuid():N}";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var databaseName = new NpgsqlConnectionStringBuilder(administrator).Database
            ?? throw new InvalidOperationException("Privileged test connection must name a database.");
        var publicTempWasGranted = await PublicTemporaryIsGrantedAsync(administrator);
        const string intruder = "openjibo_shadow_source_intruder";
        var loginCreated = false;
        var ownerCreated = false;
        var capabilityCreated = false;
        var intruderCreated = false;
        try
        {
            await ExecuteAsync(administrator, $"CREATE SCHEMA {Quote(schema)}");
            foreach (var role in new[]
                     {
                         PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.LoginRole,
                         PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.OwnerRole,
                         PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.CapabilityRole,
                         intruder
                     })
                if (await ExistsAsync(administrator, role))
                    throw new InvalidOperationException($"Privileged shadow-source test role '{role}' already exists.");
            await ExecuteAsync(administrator, $"CREATE ROLE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.LoginRole)} LOGIN INHERIT CONNECTION LIMIT 1 PASSWORD {Literal(password)}");
            loginCreated = true;
            await ExecuteAsync(administrator, $"CREATE ROLE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.OwnerRole)} NOLOGIN NOINHERIT");
            ownerCreated = true;
            await ExecuteAsync(administrator, $"CREATE ROLE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.CapabilityRole)} NOLOGIN INHERIT");
            capabilityCreated = true;
            await ExecuteAsync(administrator, $"CREATE ROLE {Quote(intruder)} NOLOGIN");
            intruderCreated = true;
            await ExecuteAsync(administrator, $"REVOKE TEMPORARY ON DATABASE {Quote(databaseName)} FROM PUBLIC");
            foreach (var migration in new[] { "011_create_runtime_usage_outbox.state.sql", "012_runtime_usage_delivery_boundary.state.sql" })
                await ExecuteAsync(administrator, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql", migration)), schema);

            await ExecuteAsync(administrator, $"GRANT {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.OwnerRole)} TO {Quote(intruder)}");
            var membership = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.ProvisionAsync(administrator, schema));
            Assert.Contains("supporting roles have unsafe membership", membership.MessageText, StringComparison.Ordinal);
            await ExecuteAsync(administrator, $"REVOKE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.OwnerRole)} FROM {Quote(intruder)}");

            await ExecuteAsync(administrator, $"GRANT SELECT ON TABLE {Quote(schema)}.RuntimeUsageDailyAccumulators TO {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.OwnerRole)}");
            var relation = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.ProvisionAsync(administrator, schema));
            Assert.Contains("raw relation ACL is contaminated", relation.MessageText, StringComparison.Ordinal);
            await ExecuteAsync(administrator, $"REVOKE ALL ON TABLE {Quote(schema)}.RuntimeUsageDailyAccumulators FROM {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.OwnerRole)}");

            await ExecuteAsync(administrator, $"GRANT EXECUTE ON FUNCTION {Quote(schema)}.ClaimRuntimeUsageOutbox(TEXT, INTEGER, INTEGER) TO {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.OwnerRole)}");
            var function = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.ProvisionAsync(administrator, schema));
            Assert.Contains("raw function ACL is contaminated", function.MessageText, StringComparison.Ordinal);
            await ExecuteAsync(administrator, $"REVOKE ALL ON FUNCTION {Quote(schema)}.ClaimRuntimeUsageOutbox(TEXT, INTEGER, INTEGER) FROM {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.OwnerRole)}");

            await ExecuteAsync(administrator, "CREATE SCHEMA runtime_usage_shadow_source");
            await ExecuteAsync(administrator, "CREATE FUNCTION runtime_usage_shadow_source.extra_shadow_wrapper() RETURNS integer LANGUAGE sql AS 'SELECT 1'");
            var wrapper = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.ProvisionAsync(administrator, schema));
            Assert.Contains("wrapper schema contains an unexpected function", wrapper.MessageText, StringComparison.Ordinal);
            await ExecuteAsync(administrator, "DROP FUNCTION runtime_usage_shadow_source.extra_shadow_wrapper()");
            await PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.ProvisionAsync(administrator, schema);
        }
        finally
        {
            await TryExecuteAsync(administrator, "RESET ROLE");
            await TryExecuteAsync(administrator, "REVOKE openjibo_runtime_usage_shadow_source_owner FROM CURRENT_USER");
            await TryExecuteAsync(administrator, "DROP SCHEMA runtime_usage_shadow_source CASCADE");
            await TryExecuteAsync(administrator, $"DROP SCHEMA {Quote(schema)} CASCADE");
            if (intruderCreated) await TryExecuteAsync(administrator, $"DROP ROLE {Quote(intruder)}");
            if (loginCreated) await TryExecuteAsync(administrator, $"DROP ROLE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.LoginRole)}");
            if (capabilityCreated) await TryExecuteAsync(administrator, $"DROP ROLE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.CapabilityRole)}");
            if (ownerCreated) await TryExecuteAsync(administrator, $"DROP ROLE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.OwnerRole)}");
            if (publicTempWasGranted) await TryExecuteAsync(administrator, $"GRANT TEMPORARY ON DATABASE {Quote(databaseName)} TO PUBLIC");
        }
    }

    [PostgreSqlPrivilegedIntegrationFact]
    [Trait("Category", "PostgreSqlPrivilegedIntegration")]
    public async Task ProvisionerExposesOnlyReadOnlyShadowFunctions()
    {
        var administrator = Environment.GetEnvironmentVariable("OPENJIBO_TEST_POSTGRES_PRIVILEGED_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set OPENJIBO_TEST_POSTGRES_PRIVILEGED_CONNECTION_STRING.");
        var schema = $"oj_shadow_test_{Guid.NewGuid():N}";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var databaseName = new NpgsqlConnectionStringBuilder(administrator).Database
            ?? throw new InvalidOperationException("Privileged test connection must name a database.");
        var publicTempWasGranted = await PublicTemporaryIsGrantedAsync(administrator);
        var loginCreated = false;
        var ownerExisted = false;
        var capabilityExisted = false;
        try
        {
            await ExecuteAsync(administrator, $"CREATE SCHEMA {Quote(schema)}");
            ownerExisted = await ExistsAsync(administrator, PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.OwnerRole);
            capabilityExisted = await ExistsAsync(administrator, PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.CapabilityRole);
            if (await ExistsAsync(administrator, PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.LoginRole))
                throw new InvalidOperationException("Privileged shadow-source login already exists.");
            await ExecuteAsync(administrator, $"CREATE ROLE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.LoginRole)} LOGIN INHERIT CONNECTION LIMIT 1 PASSWORD {Literal(password)}");
            loginCreated = true;
            await ExecuteAsync(administrator, $"REVOKE TEMPORARY ON DATABASE {Quote(databaseName)} FROM PUBLIC");
            foreach (var migration in new[] { "011_create_runtime_usage_outbox.state.sql", "012_runtime_usage_delivery_boundary.state.sql" })
                await ExecuteAsync(administrator, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql", migration)), schema);

            await PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.ProvisionAsync(administrator, schema);
            await PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.ProvisionAsync(administrator, schema);
            await ExecuteAsync(administrator, """
                INSERT INTO RuntimeUsageDailyAccumulators(ManagedRobotId,UsageDate,ServiceEnvironment,SuccessfulTurns,FirstEventUtc,LastEventUtc,Revision)
                VALUES('11111111-1111-1111-1111-111111111111','2026-09-22','staging',1,'2026-09-22T01:00:00Z','2026-09-22T01:00:00Z',1);
                INSERT INTO RuntimeUsageOutboxMessages(MessageId,ManagedRobotId,UsageDate,ServiceEnvironment,SourceSequence,AccumulatorRevision,FormatVersion,SourceSchemaVersion,SourceRevision,SuccessfulTurns,FailedTurns,HttpRequests,HttpRequestBytes,HttpResponseBytes,WebSocketInboundMessages,WebSocketOutboundMessages,WebSocketInboundBytes,WebSocketOutboundBytes,AudioInputBytes,FirstEventUtc,LastEventUtc,IsIncomplete,FirstIncompleteUtc,IncompleteReasonCode,IdempotencyKey)
                VALUES('22222222-2222-2222-2222-222222222222','11111111-1111-1111-1111-111111111111','2026-09-22','staging',1,1,1,'openjibo-runtime-usage.v1','1',1,0,0,0,0,0,0,0,0,0,'2026-09-22T01:00:00Z','2026-09-22T01:00:00Z',FALSE,NULL,NULL,repeat('A',43));
                INSERT INTO RuntimeUsageOutboxDelivery(MessageId) VALUES('22222222-2222-2222-2222-222222222222');
                """, schema);
            var reader = new NpgsqlConnectionStringBuilder(administrator) { Username = PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.LoginRole, Password = password, Pooling = false }.ConnectionString;
            Assert.Equal(1L, await ScalarAsync<long>(reader, "SELECT message_high_watermark_sequence FROM runtime_usage_shadow_source.describe_runtime_usage_stream('11111111-1111-1111-1111-111111111111','2026-09-22','staging')"));
            Assert.Equal(1L, await ScalarAsync<long>(reader, "SELECT source_sequence FROM runtime_usage_shadow_source.read_runtime_usage_stream_page('11111111-1111-1111-1111-111111111111','2026-09-22','staging',0,1,250)"));
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, await TryExecuteAsync(reader, "SELECT * FROM runtime_usage_shadow_source.read_runtime_usage_stream_page('11111111-1111-1111-1111-111111111111','2026-09-22','staging',0,2,250)"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await TryExecuteAsync(reader, $"SELECT * FROM {Quote(schema)}.RuntimeUsageOutboxMessages"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await TryExecuteAsync(reader, $"SET ROLE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.CapabilityRole)}"));
            Assert.False(await ScalarAsync<bool>(administrator, $"SELECT has_table_privilege('{PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.LoginRole}','{schema}.RuntimeUsageOutboxDelivery','SELECT')"));
        }
        finally
        {
            await TryExecuteAsync(administrator, "RESET ROLE");
            await TryExecuteAsync(administrator, "REVOKE openjibo_runtime_usage_shadow_source_owner FROM CURRENT_USER");
            await TryExecuteAsync(administrator, "DROP SCHEMA runtime_usage_shadow_source CASCADE");
            await TryExecuteAsync(administrator, $"DROP SCHEMA {Quote(schema)} CASCADE");
            if (loginCreated) await TryExecuteAsync(administrator, $"DROP ROLE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.LoginRole)}");
            if (!capabilityExisted) await TryExecuteAsync(administrator, $"DROP ROLE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.CapabilityRole)}");
            if (!ownerExisted) await TryExecuteAsync(administrator, $"DROP ROLE {Quote(PostgreSqlRuntimeUsageShadowSourceRoleProvisioner.OwnerRole)}");
            if (publicTempWasGranted) await TryExecuteAsync(administrator, $"GRANT TEMPORARY ON DATABASE {Quote(databaseName)} TO PUBLIC");
        }
    }

    private static async Task ExecuteAsync(string connection, string sql, string? searchPath = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(connection); if (searchPath is not null) builder.SearchPath = searchPath;
        await using var database = new NpgsqlConnection(builder.ConnectionString); await database.OpenAsync(); await using var command = new NpgsqlCommand(sql, database); await command.ExecuteNonQueryAsync();
    }
    private static async Task<T> ScalarAsync<T>(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection); await database.OpenAsync(); await using var command = new NpgsqlCommand(sql, database);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
    private static async Task<bool> ExistsAsync(string connection, string role) => await ScalarAsync<bool>(connection, $"SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={Literal(role)})");
    private static async Task<bool> PublicTemporaryIsGrantedAsync(string connection) => await ScalarAsync<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(d.datacl,pg_catalog.acldefault('d',d.datdba))) a WHERE d.datname=current_database() AND a.grantee=0 AND a.privilege_type='TEMPORARY')");
    private static async Task<string?> TryExecuteAsync(string connection, string sql) { try { await ExecuteAsync(connection, sql); return null; } catch (PostgresException error) { return error.SqlState; } }
    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    private static string Literal(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
