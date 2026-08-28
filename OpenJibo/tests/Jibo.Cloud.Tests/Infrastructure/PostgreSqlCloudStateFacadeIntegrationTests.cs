using System.Diagnostics;
using System.Text;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Npgsql;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PostgreSqlCloudStateFacadeIntegrationTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task HubToken_PreservesUnlinkedObservedIdentityWithoutCreatingInventory()
    {
        await using var database = await CloudStateTestDatabase.CreateAsync();
        await using var source = new PostgreSqlCloudStateDataSource(database.ConnectionString, 2);
        var store = new PostgreSqlCloudStateStore(source, new PlaintextTestProtector());

        var token = store.IssueHubToken("unlinked-observed-device");
        var session = store.OpenSession(
            "neo-hub-listen",
            null,
            token,
            "neohub.openjibo.com",
            "/v1/listen");

        Assert.Equal("unlinked-observed-device", session.DeviceId);
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM CloudAuthTokens WHERE DeviceId='unlinked-observed-device'"));
        Assert.Equal(0, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM Devices WHERE DeviceId='unlinked-observed-device'"));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task IndependentStores_ObserveCommittedScopedChangesWithoutSnapshotOrSessionWrites()
    {
        await using var database = await CloudStateTestDatabase.CreateAsync();
        await using var sourceA = new PostgreSqlCloudStateDataSource(database.ConnectionString, 2);
        await using var sourceB = new PostgreSqlCloudStateDataSource(database.ConnectionString, 2);
        var protector = new PlaintextTestProtector();
        var ttl = TimeSpan.FromMilliseconds(40);
        var first = new PostgreSqlCloudStateStore(sourceA, protector, deviceCacheMaxEntries: 8,
            deviceCacheTtl: ttl, maximumActiveSessions: 4);
        var second = new PostgreSqlCloudStateStore(sourceB, protector, deviceCacheMaxEntries: 8,
            deviceCacheTtl: ttl, maximumActiveSessions: 4);

        first.UpsertDevice(Device("shared-device", "Shared Before"));
        first.UpsertDevice(Device("unrelated-device", "Must Survive"));
        Assert.Equal("Shared Before", second.GetOrCreateDevice("shared-device", null, null).FriendlyName);
        await database.ExecuteAsync("""
            INSERT INTO PersistenceSnapshots (SnapshotName, SnapshotJson)
            VALUES ('cloud-state-integration-marker', '{"marker":"unchanged"}')
            """);
        var snapshotBefore = await database.ReadSnapshotMarkerAsync();

        first.RenameDevice("shared-device", "Shared After");
        await Task.Delay(ttl + TimeSpan.FromMilliseconds(80));

        Assert.Equal("Shared After", second.GetOrCreateDevice("shared-device", null, null).FriendlyName);
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM Devices WHERE DeviceId='unrelated-device' AND FriendlyName='Must Survive'"));
        Assert.Equal(snapshotBefore, await database.ReadSnapshotMarkerAsync());

        var revisionBeforeSession = await database.ExecuteScalarAsync<long>(
            "SELECT Revision FROM CloudStateMetadata WHERE StateKey='cloud-state'");
        var tokensBefore = await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM CloudAuthTokens");
        var active = second.OpenSession("robot", "shared-device", null, "socket.test", "/v1/listen");
        Assert.False(string.IsNullOrWhiteSpace(active.Token));
        Assert.Same(active, second.FindActiveSessionByToken(active.Token!));
        second.CloseSession(active.SessionId);

        Assert.Equal(revisionBeforeSession, await database.ExecuteScalarAsync<long>(
            "SELECT Revision FROM CloudStateMetadata WHERE StateKey='cloud-state'"));
        Assert.Equal(tokensBefore, await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM CloudAuthTokens"));
        Assert.Equal(snapshotBefore, await database.ReadSnapshotMarkerAsync());
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task FreshBootstrap_PreservesLargeUnscopedFleetAndAddsOnlyItsScopedDefaults()
    {
        await using var database = await CloudStateTestDatabase.CreateAsync();
        const int fleetSize = 1200;
        await database.PreseedFleetAsync(fleetSize);
        await using var source = new PostgreSqlCloudStateDataSource(database.ConnectionString, 2);

        var store = new PostgreSqlCloudStateStore(source, new PlaintextTestProtector(), deviceCacheMaxEntries: 4,
            deviceCacheTtl: TimeSpan.FromMilliseconds(20), maximumActiveSessions: 2);

        Assert.Equal(fleetSize + 1, await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Devices"));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM Devices WHERE DeviceId='openjibo-bootstrap-default'"));
        Assert.Single(store.GetDevices());
        Assert.Single(store.GetLoops());
        Assert.Equal(fleetSize, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM Devices WHERE DeviceId LIKE 'fleet-device-%'"));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task AdministrationInventory_SeesEveryAccountWithoutBroadeningNormalReads()
    {
        await using var database = await CloudStateTestDatabase.CreateAsync();
        await using var source = new PostgreSqlCloudStateDataSource(database.ConnectionString, 2);
        var store = new PostgreSqlCloudStateStore(source, new PlaintextTestProtector());
        await database.ExecuteAsync("""
            INSERT INTO Accounts (AccountId,Email,AccessKeyId,IsDefault)
            VALUES ('other-account','other@example.invalid','other-access',FALSE);
            INSERT INTO Devices (DeviceId,RobotId,FriendlyName,RegistrationSource,IsHidden,ArchivedUtc)
            VALUES ('other-visible','duplicate-robot-name','Duplicate Robot','physical',FALSE,NULL),
                   ('other-archived','duplicate-robot-name','Duplicate Robot','physical',TRUE,NOW());
            INSERT INTO AccountDevices (AccountId,DeviceId)
            VALUES ('other-account','other-visible'),('other-account','other-archived');
            """);

        Assert.DoesNotContain(store.GetDevices(), device => device.DeviceId.StartsWith("other-", StringComparison.Ordinal));
        var administration = store.GetDevicesForAdministration();
        Assert.Contains(administration, device => device.DeviceId == "other-visible");
        Assert.Contains(administration, device => device.DeviceId == "other-archived" && device.IsHidden);
        Assert.Equal(2, administration.Count(device => device.FriendlyName == "Duplicate Robot"));

        var visible = administration.Single(device => device.DeviceId == "other-visible");
        store.UpsertDeviceForAdministration(new DeviceRegistration
        {
            DeviceId = visible.DeviceId, RobotId = visible.RobotId, FriendlyName = visible.FriendlyName,
            RegistrationSource = visible.RegistrationSource, IsHidden = true, ArchivedUtc = DateTimeOffset.UtcNow
        });
        Assert.Equal(0, await database.ExecuteScalarAsync<long>("""
            SELECT COUNT(*) FROM AccountDevices ad JOIN Accounts a ON a.AccountId=ad.AccountId
            WHERE ad.DeviceId='other-visible' AND a.IsDefault
            """));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM AccountDevices WHERE DeviceId='other-visible'"));

        store.RenameDeviceForAdministration("other-visible", "Other-Renamed-Robot");
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM AccountDevices WHERE DeviceId='other-visible' AND AccountId='other-account'"));
        Assert.Throws<InvalidOperationException>(() =>
            store.MergeRobotRecordsForAdministration("other-visible", "openjibo-bootstrap-default"));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM AccountDevices WHERE DeviceId='other-visible' AND AccountId='other-account'"));

        await database.ExecuteAsync("""
            INSERT INTO Devices (DeviceId,RobotId,FriendlyName,RegistrationSource)
            VALUES ('single-source','single-source','Single Source','physical'),
                   ('single-target','single-target','Single Target','physical'),
                   ('multi-source','multi-source','Multi Source','physical'),
                   ('multi-target','multi-target','Multi Target','physical'),
                   ('zero-source','zero-source','Zero Source','physical'),
                   ('zero-target','zero-target','Zero Target','physical');
            INSERT INTO AccountDevices (AccountId,DeviceId)
            VALUES ('other-account','single-source'),('other-account','single-target'),
                   ('other-account','multi-source'),('other-account','multi-target'),
                   ('usr_openjibo_owner','multi-source'),('usr_openjibo_owner','multi-target');
            """);
        store.MergeRobotRecordsForAdministration("single-source", "single-target");
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM AccountDevices WHERE DeviceId='single-source' AND AccountId='other-account'"));
        store.MergeRobotRecordsForAdministration("multi-source", "multi-target");
        Assert.Equal(2, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM AccountDevices WHERE DeviceId='multi-source'"));
        store.MergeRobotRecordsForAdministration("zero-source", "zero-target");
        Assert.Equal(0, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM AccountDevices WHERE DeviceId IN ('zero-source','zero-target')"));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task TwoBackups_CreateBoundedManifestsAndExternalPayloadsWithoutSnapshotRewrite()
    {
        await using var database = await CloudStateTestDatabase.CreateAsync();
        await using var source = new PostgreSqlCloudStateDataSource(database.ConnectionString, 2);
        var payloadRoot = Path.Combine(Path.GetTempPath(), $"openjibo-cloud-backup-test-{Guid.NewGuid():N}");
        try
        {
            var store = new PostgreSqlCloudStateStore(source, new PlaintextTestProtector(),
                backupPayloadStore: new DirectoryBackupPayloadStore(payloadRoot));
            await database.ExecuteAsync("""
                INSERT INTO PersistenceSnapshots (SnapshotName, SnapshotJson)
                VALUES ('cloud-state-integration-marker', '{"marker":"backup-stable"}')
                """);
            var snapshotBefore = await database.ReadSnapshotMarkerAsync();
            var loopId = Assert.Single(store.GetLoops()).LoopId;

            var first = store.CreateBackup(loopId, "first");
            var second = store.CreateBackup(loopId, "second");

            Assert.NotEqual(first.BackupId, second.BackupId);
            Assert.Null(first.SnapshotJson);
            Assert.Null(second.SnapshotJson);
            Assert.Equal(2, store.GetBackups().Count);
            Assert.Equal(2, await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM BackupManifests"));
            Assert.Equal(2, Directory.GetFiles(payloadRoot, "*.json", SearchOption.AllDirectories).Length);
            Assert.True(await database.ExecuteScalarAsync<long>(
                "SELECT MAX(OCTET_LENGTH(BlobUri)+OCTET_LENGTH(ContentSha256)+OCTET_LENGTH(Name)) FROM BackupManifests") < 2048);
            Assert.Equal(snapshotBefore, await database.ReadSnapshotMarkerAsync());
        }
        finally
        {
            if (Directory.Exists(payloadRoot)) Directory.Delete(payloadRoot, recursive: true);
        }
    }

    private static DeviceRegistration Device(string id, string name) => new()
    {
        DeviceId = id, RobotId = name, FriendlyName = name,
        RegistrationSource = RobotRegistrationSources.Physical
    };

    private sealed class PlaintextTestProtector : ICloudStateSecretProtector
    {
        public string KeyId => "integration-test-plaintext";
        public byte[] Protect(string plaintext) => Encoding.UTF8.GetBytes(plaintext);
        public string Unprotect(byte[] ciphertext) => Encoding.UTF8.GetString(ciphertext);
    }

    private sealed class CloudStateTestDatabase : IAsyncDisposable
    {
        private const string ConnectionVariable = "OPENJIBO_TEST_POSTGRES_CONNECTION_STRING";
        private readonly string _adminConnectionString;
        private readonly string _schemaName;

        private CloudStateTestDatabase(string adminConnectionString, string schemaName)
        {
            _adminConnectionString = adminConnectionString;
            _schemaName = schemaName;
            ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                SearchPath = schemaName,
                ApplicationName = "OpenJibo.CloudState.IntegrationTests",
                MaxPoolSize = 4
            }.ConnectionString;
        }

        internal string ConnectionString { get; }

        internal static async Task<CloudStateTestDatabase> CreateAsync()
        {
            var admin = Environment.GetEnvironmentVariable(ConnectionVariable);
            if (string.IsNullOrWhiteSpace(admin)) throw new InvalidOperationException($"Set {ConnectionVariable}.");
            var schema = $"openjibo_cloud_test_{Guid.NewGuid():N}";
            var database = new CloudStateTestDatabase(admin, schema);
            await using (var connection = new NpgsqlConnection(admin))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE SCHEMA {QuoteIdentifier(schema)}";
                await command.ExecuteNonQueryAsync();
            }
            try
            {
                await database.ApplyMigrationsAsync();
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        internal async Task ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<T> ExecuteScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(value!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        internal async Task<(string Json, DateTimeOffset UpdatedUtc)> ReadSnapshotMarkerAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT SnapshotJson, UpdatedUtc FROM PersistenceSnapshots WHERE SnapshotName='cloud-state-integration-marker'";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1));
        }

        internal async Task PreseedFleetAsync(int count)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Devices (DeviceId,RobotId,FriendlyName,RegistrationSource,IsDefault)
                SELECT 'fleet-device-'||value, 'fleet-robot-'||value, 'Fleet Robot '||value, 'physical', FALSE
                FROM generate_series(1, @count) value;
                """;
            command.Parameters.AddWithValue("count", count);
            await command.ExecuteNonQueryAsync();
            Assert.Equal(count, await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Devices"));
        }

        private async Task ApplyMigrationsAsync()
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql");
            var scripts = Directory.GetFiles(directory, "*.sql")
                .Where(path => !path.EndsWith(".personal-memory.sql", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            foreach (var script in scripts)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = await File.ReadAllTextAsync(script);
                await command.ExecuteNonQueryAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA {QuoteIdentifier(_schemaName)} CASCADE";
            await command.ExecuteNonQueryAsync();
        }

        private static string QuoteIdentifier(string identifier)
        {
            Debug.Assert(identifier.StartsWith("openjibo_cloud_test_", StringComparison.Ordinal));
            return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }
    }
}
