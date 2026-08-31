using Jibo.Cloud.Infrastructure.Persistence;
using Npgsql;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PostgreSqlCloudStateAuditorIntegrationTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task AuditAsync_ReportsBackupSchemasAndIdentityIntegrityWithoutIdentifiers()
    {
        const string variable = "OPENJIBO_TEST_POSTGRES_CONNECTION_STRING";
        var adminConnectionString = Environment.GetEnvironmentVariable(variable)
                                    ?? throw new InvalidOperationException($"Set {variable}.");
        var schema = $"openjibo_cloud_audit_{Guid.NewGuid():N}";
        await ExecuteAdminAsync(adminConnectionString, $"CREATE SCHEMA \"{schema}\"");
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema };
        var scopedConnectionString = builder.ConnectionString;

        try
        {
            await ApplyStateMigrationsAsync(scopedConnectionString);
            await using (var connection = new NpgsqlConnection(scopedConnectionString))
            {
                await connection.OpenAsync();
                await using var seed = connection.CreateCommand();
                seed.CommandText = """
                                   INSERT INTO Accounts
                                     (AccountId, Email, FirstName, LastName, AccessKeyId, IsDefault)
                                   VALUES ('account-1', 'owner@example.com', '', '', 'access-1', TRUE);

                                   INSERT INTO Devices
                                     (DeviceId, RobotId, FriendlyName, IsActive, IsHidden, IsDefault, ArchivedUtc)
                                   VALUES
                                     ('device-linked', 'robot-linked', 'Linked', TRUE, FALSE, TRUE, NULL),
                                     ('device-unlinked', 'robot-unlinked', 'Unlinked', TRUE, FALSE, FALSE, NULL),
                                     ('device-archived', 'robot-archived', 'Archived', FALSE, FALSE, FALSE, NOW());

                                   INSERT INTO AccountDevices (AccountId, DeviceId)
                                   VALUES ('account-1', 'device-linked');

                                   INSERT INTO RobotProfiles (RobotId, DeviceId)
                                   VALUES
                                     ('robot-without-device', NULL),
                                     ('robot-does-not-match', 'device-linked');

                                   INSERT INTO RobotIdentityLinks
                                     (ObservedDeviceId, InventoryDeviceId, ClaimSource)
                                   VALUES
                                     ('observed-linked', 'device-linked', 'test'),
                                     ('observed-archived', 'device-archived', 'test');

                                   INSERT INTO BackupManifests
                                     (BackupId, AccountId, Name, BlobUri, ContentSha256, ContentLength,
                                      BackupSchemaVersion, Status)
                                   VALUES
                                     ('backup-v1', 'account-1', 'v1', 'file:///v1', repeat('1', 64), 10, 1, 'available'),
                                     ('backup-v2', 'account-1', 'v2', 'file:///v2', repeat('2', 64), 20, 2, 'restored'),
                                     ('backup-v3', 'account-1', 'v3', 'file:///v3', repeat('3', 64), 30, 3, 'available');
                                   """;
                await seed.ExecuteNonQueryAsync();
            }

            var report = await PostgreSqlCloudStateAuditor.AuditAsync(scopedConnectionString);

            Assert.False(report.SnapshotPresent);
            Assert.Empty(report.Legacy);
            Assert.Equal(3, report.Normalized["devices"]);
            Assert.Equal(3, report.Normalized["backups"]);
            Assert.Equal(1, report.Integrity["accountDeviceLinks"]);
            Assert.Equal(2, report.Integrity["devicesWithoutAccountLinks"]);
            Assert.Equal(1, report.Integrity["activeVisibleDevicesWithoutAccountLinks"]);
            Assert.Equal(1, report.Integrity["robotProfilesWithoutDeviceLinks"]);
            Assert.Equal(1, report.Integrity["robotProfilesWithMismatchedRobotIds"]);
            Assert.Equal(2, report.Integrity["activeRobotIdentityLinks"]);
            Assert.Equal(1, report.Integrity["activeIdentityLinksToInactiveDevices"]);
            Assert.Equal(0, report.Integrity["duplicateActiveRobotIds"]);
            Assert.Equal(1, report.Integrity["defaultAccounts"]);
            Assert.Equal(1, report.Integrity["defaultDevices"]);
            Assert.Equal(1, report.Integrity["backupSchemaV1"]);
            Assert.Equal(1, report.Integrity["backupSchemaV2"]);
            Assert.Equal(1, report.Integrity["unsupportedBackupSchemas"]);
            Assert.Equal(2, report.Integrity["availableBackups"]);
        }
        finally
        {
            await ExecuteAdminAsync(adminConnectionString, $"DROP SCHEMA \"{schema}\" CASCADE");
        }
    }

    private static async Task ApplyStateMigrationsAsync(string connectionString)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql");
        var scripts = Directory.GetFiles(directory, "*.sql")
            .Where(path => !path.EndsWith(".personal-memory.sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var script in scripts)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(script);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecuteAdminAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
