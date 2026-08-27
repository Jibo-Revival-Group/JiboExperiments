using System.Text.Json;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Npgsql;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class CloudStateSnapshotImporterTests
{
    [Fact]
    public void ParseSnapshot_MapsEveryDurableTopLevelFamilyAndExcludesLiveSessions()
    {
        const string json = """
                            {
                              "SchemaVersion": "1",
                              "Revision": 42,
                              "Account": { "AccountId": "account-1", "Email": "owner@example.com",
                                "AccessKeyId": "access", "SecretAccessKey": "account-secret" },
                              "Robot": { "DeviceId": "device-1", "RobotId": "robot-1", "FriendlyName": "Jibo" },
                              "RobotProfile": { "RobotId": "robot-1", "Payload": { "platform": "12.10" } },
                              "Devices": [
                                { "DeviceId": "device-1", "RobotId": "robot-1", "FriendlyName": "Jibo" },
                                { "DeviceId": "device-2", "RobotId": "robot-2", "FriendlyName": "Other" }
                              ],
                              "RobotCredentialBindings": [
                                { "AccessKeyFingerprint": "fingerprint", "DeviceId": "device-1",
                                  "ClaimedUtc": "2026-08-20T00:00:00Z", "ClaimSource": "portal" }
                              ],
                              "Sessions": [
                                { "SessionId": "issued", "Kind": "robot", "AccountId": "account-1",
                                  "DeviceId": "observed-1", "Token": "token-device-1-secret",
                                  "CreatedUtc": "2026-08-20T00:00:00Z",
                                  "Metadata": { "registeredDeviceId": "device-1" } },
                                { "SessionId": "hub", "Kind": "hub", "AccountId": "account-1",
                                  "DeviceId": "device-1", "Token": "hub-account-1-secret",
                                  "CreatedUtc": "2026-08-20T00:00:00Z" },
                                { "SessionId": "live", "Kind": "neo-hub-listen", "DeviceId": "device-1",
                                  "Token": "conn:live", "LastTranscript": "must not import" }
                              ],
                              "SymmetricKeys": { "loop-1": "plaintext-loop-key" },
                              "KeyRequests": [{ "RequestId": "request-1", "LoopId": "loop-1" }],
                              "Updates": [{ "UpdateId": "update-1" }],
                              "Media": [{ "Path": "photo-1", "AccountId": "account-1", "LoopId": "loop-1" }],
                              "Backups": [{ "BackupId": "backup-1", "LoopId": "loop-1", "SnapshotJson": "{}" }],
                              "CommuteProfiles": [{ "Id": "commute-1", "LoopId": "loop-1" }],
                              "CalendarEvents": [{ "Id": "calendar-1", "LoopId": "loop-1" }],
                              "GreetingPresences": [{ "Id": "greeting-1", "AccountId": "account-1",
                                "LoopId": "loop-1", "PersonId": "person-1" }],
                              "Loops": [{ "LoopId": "loop-1", "OwnerAccountId": "account-1",
                                "RobotId": "robot-1", "RobotFriendlyId": "device-1" }],
                              "Holidays": [{ "Id": "holiday-1", "LoopId": "loop-1" }],
                              "LoopMembers": [{ "Id": "member-1", "LoopId": "loop-1" }],
                              "People": [{ "PersonId": "person-1", "AccountId": "account-1",
                                "LoopId": "loop-1", "RobotId": "robot-1" }],
                              "Users": [{ "Id": "user-1", "Email": "user@example.com",
                                "PasswordHash": "hash", "Salt": "salt", "AccessKeyId": "user-access",
                                "SecretAccessKey": "user-secret" }],
                              "RecognitionObservations": [{ "ObservationId": "observation-1", "LoopId": "loop-1",
                                "MemberId": "member-1", "RobotId": "robot-1" }],
                              "RevokedIdentityGraphAnchors": ["anchor-1"],
                              "TrustedServers": [{ "ServerId": "server-1", "CanonicalHost": "cloud.example" }],
                              "TrustedServerAdmissions": [{ "AdmissionId": "admission-1", "ServerId": "server-1",
                                "CanonicalHost": "cloud.example" }]
                            }
                            """;

        var snapshot = PostgreSqlCloudStateSnapshotImporter.ParseSnapshot(json);
        var counts = snapshot.GetDurableFamilyCounts();

        Assert.Equal("1", snapshot.SchemaVersion);
        Assert.Equal(42, snapshot.Revision);
        Assert.Equal(2, counts["devices"]); // Robot is de-duplicated against Devices.
        Assert.Equal(2, counts["issuedTokens"]);
        Assert.Equal(1, counts["robotIdentityLinks"]);
        Assert.Equal(1, counts["accounts"]);
        Assert.Equal(1, counts["robotProfiles"]);
        Assert.Equal(1, counts["robotCredentialBindings"]);
        Assert.Equal(1, counts["symmetricKeys"]);
        Assert.Equal(1, counts["keyRequests"]);
        Assert.Equal(1, counts["updates"]);
        Assert.Equal(1, counts["media"]);
        Assert.Equal(1, counts["backups"]);
        Assert.Equal(1, counts["commuteProfiles"]);
        Assert.Equal(1, counts["calendarEvents"]);
        Assert.Equal(1, counts["greetingPresences"]);
        Assert.Equal(1, counts["loops"]);
        Assert.Equal(1, counts["holidays"]);
        Assert.Equal(1, counts["loopMembers"]);
        Assert.Equal(1, counts["people"]);
        Assert.Equal(1, counts["users"]);
        Assert.Equal(1, counts["recognitionObservations"]);
        Assert.Equal(1, counts["revokedIdentityGraphAnchors"]);
        Assert.Equal(1, counts["trustedServers"]);
        Assert.Equal(1, counts["trustedServerAdmissions"]);
        Assert.DoesNotContain(snapshot.IssuedTokenSessions(), session => session.Token == "conn:live");
        Assert.Equal("device-1", snapshot.IssuedTokenSessions().Single(s => s.Kind == "robot")
            .TryGetRegisteredDeviceId());
        var durableMetadata = snapshot.IssuedTokenSessions().Single(s => s.Kind == "robot").DurableTokenMetadata();
        Assert.Equal("device-1", ((JsonElement)durableMetadata["registeredDeviceId"]!).GetString());
        Assert.DoesNotContain("LastTranscript", durableMetadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetLegacyDurableFamilyCounts_EmitsOnlyAggregateCounts()
    {
        const string json = """
                              {
                                "Robot": { "DeviceId": "device-a", "RobotId": "robot-a" },
                                "Devices": [
                                  { "DeviceId": "DEVICE-A", "RobotId": "robot-a" },
                                  { "DeviceId": "device-b", "RobotId": "robot-b" }
                                ],
                                "Sessions": [
                                  { "DeviceId": "observed-a", "Token": "token-secret",
                                    "Metadata": { "registeredDeviceId": "device-a" } },
                                  { "DeviceId": "device-b", "Token": "conn:live" }
                                ]
                              }
                              """;

        var counts = PostgreSqlCloudStateSnapshotImporter.GetLegacyDurableFamilyCounts(json);

        Assert.Equal(2, counts["devices"]);
        Assert.Equal(1, counts["issuedTokens"]);
        Assert.Equal(1, counts["robotIdentityLinks"]);
        Assert.DoesNotContain("device-a", counts.Keys, StringComparer.OrdinalIgnoreCase);
    }
    [Fact]
    public void TokenHashAndIdentityAudit_DoNotPersistReusableTokenOrObjectShapedAudit()
    {
        const string token = "token-device-1-super-secret";
        var hash = PostgreSqlCloudStateSnapshotImporter.Sha256(token);
        var occurred = new DateTimeOffset(2026, 8, 20, 1, 2, 3, TimeSpan.Zero);
        var audit = PostgreSqlCloudStateSnapshotImporter.BuildLegacyIdentityLinkAudit("device-1", occurred);
        using var auditJson = JsonDocument.Parse(JsonSerializer.Serialize(audit));

        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain(token, hash, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Array, auditJson.RootElement.ValueKind);
        var entry = Assert.Single(audit);
        Assert.Equal("linked", entry.Action);
        Assert.Null(entry.PreviousInventoryDeviceId);
        Assert.Equal("device-1", entry.InventoryDeviceId);
        Assert.Equal("legacy-cloud-state-import", entry.Source);
        Assert.Equal(occurred, entry.OccurredUtc);
    }

    [Fact]
    public void ParseSnapshot_RejectsMalformedJson()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => PostgreSqlCloudStateSnapshotImporter.ParseSnapshot("{ not-json"));

        Assert.Contains("not valid JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanonicalUsers_PreservesFirstCaseInsensitiveEmailMatch()
    {
        var first = new UserRecord { Id = "first", Email = "User@Example.com" };
        var duplicate = new UserRecord { Id = "duplicate", Email = " user@example.COM " };
        var distinct = new UserRecord { Id = "distinct", Email = "other@example.com" };

        var canonical = PostgreSqlCloudStateSnapshotImporter.CanonicalUsers([first, duplicate, distinct]);

        Assert.Equal([first, distinct], canonical);
    }

    [Fact]
    public async Task ExportBackupPayloads_UsesDeterministicVerifiedExternalPayloads()
    {
        var sink = new RecordingBackupPayloadStore();
        var backup = new BackupRecord
        {
            BackupId = "backup-1",
            LoopId = "loop-1",
            Name = "Before migration",
            SnapshotJson = "{\"Revision\":7}"
        };

        var first = Assert.Single(await PostgreSqlCloudStateSnapshotImporter.ExportBackupPayloadsAsync(
            [backup], sink));
        var second = Assert.Single(await PostgreSqlCloudStateSnapshotImporter.ExportBackupPayloadsAsync(
            [backup], sink));

        Assert.Equal(first.Uri, second.Uri);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(backup.SnapshotJson!.Length, first.Length);
        Assert.Equal(backup.SnapshotJson, System.Text.Encoding.UTF8.GetString(sink.Payloads[first.Uri]));
        Assert.Contains("backup-1", first.Uri, StringComparison.Ordinal);
        Assert.Contains(first.Sha256, first.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportBackupPayloads_RequiresSinkAndRejectsCorruptReadBack()
    {
        var backup = new BackupRecord { BackupId = "backup-unsafe", SnapshotJson = "{\"Revision\":8}" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgreSqlCloudStateSnapshotImporter.ExportBackupPayloadsAsync([backup], null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgreSqlCloudStateSnapshotImporter.ExportBackupPayloadsAsync(
                [backup], new RecordingBackupPayloadStore { CorruptOnLoad = true }));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task ImportAsync_IsTransactionalIdempotentAndPreservesSourceSnapshot()
    {
        const string variable = "OPENJIBO_TEST_POSTGRES_CONNECTION_STRING";
        var adminConnectionString = Environment.GetEnvironmentVariable(variable)
                                    ?? throw new InvalidOperationException($"Set {variable}.");
        var schema = $"openjibo_cloud_import_{Guid.NewGuid():N}";
        await ExecuteAdminAsync(adminConnectionString, $"CREATE SCHEMA \"{schema}\"");
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema };
        var scopedConnectionString = builder.ConnectionString;

        try
        {
            await ApplyStateMigrationsAsync(scopedConnectionString);
            const string token = "token-observed-runtime-secret";
            var sourceJson = $$$"""
                               {
                                 "SchemaVersion":"1", "Revision":9,
                                 "Account":{"AccountId":"account-1","Email":"owner@example.com",
                                   "AccessKeyId":"access-1","SecretAccessKey":"secret-1"},
                                 "Robot":{"DeviceId":"device-1","RobotId":"robot-1","FriendlyName":"Jibo"},
                                 "Devices":[{"DeviceId":"device-1","RobotId":"robot-1","FriendlyName":"Jibo"}],
                                 "Users":[{"Id":"legacy-user","Email":"existing@example.com",
                                   "PasswordHash":"legacy-hash","Salt":"legacy-salt",
                                   "AccessKeyId":"legacy-access","SecretAccessKey":"legacy-secret"}],
                                 "Loops":[{"LoopId":"loop-1","OwnerAccountId":"account-1",
                                   "RobotId":"robot-1","RobotFriendlyId":"device-1"}],
                                 "People":[{"PersonId":"orphan-person","AccountId":"missing-account",
                                   "LoopId":"loop-1","RobotId":"robot-1","DisplayName":"Orphan"}],
                                 "Sessions":[{"SessionId":"issued","Kind":"robot","AccountId":"account-1",
                                   "DeviceId":"observed-runtime","Token":"{{{token}}}",
                                   "CreatedUtc":"2026-08-20T00:00:00Z",
                                   "Metadata":{"registeredDeviceId":"device-1","LastTranscript":"do not persist"}}]
                               }
                               """;
            await using (var connection = new NpgsqlConnection(scopedConnectionString))
            {
                await connection.OpenAsync();
                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                                     INSERT INTO PersistenceSnapshots (SnapshotName,SnapshotJson)
                                     VALUES ('cloud-state',@json);
                                     INSERT INTO Users (UserId,Email,PasswordHash,PasswordSalt,AccessKeyId)
                                     VALUES ('existing-user','Existing@Example.com','hash','salt','existing-access')
                                     """;
                insert.Parameters.AddWithValue("json", sourceJson);
                await insert.ExecuteNonQueryAsync();
            }

            await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
            var importer = new PostgreSqlCloudStateSnapshotImporter(
                dataSource,
                new UserDataCloudStateSecretProtector(
                    new UserDataEncryptionService("integration-passphrase", "integration-salt")));
            var first = await importer.ImportAsync();
            var second = await importer.ImportAsync();

            Assert.False(first.AlreadyImported);
            Assert.True(second.AlreadyImported);
            Assert.Equal(first.SourceSha256, second.SourceSha256);
            Assert.Equal(sourceJson, await ScalarAsync<string>(scopedConnectionString,
                "SELECT SnapshotJson FROM PersistenceSnapshots WHERE SnapshotName='cloud-state'"));
            Assert.Equal(1, await ScalarAsync<long>(scopedConnectionString, "SELECT COUNT(*) FROM CloudStateImports"));
            Assert.Equal(1, await ScalarAsync<long>(scopedConnectionString, "SELECT COUNT(*) FROM Accounts"));
            Assert.Equal(1, await ScalarAsync<long>(scopedConnectionString, "SELECT COUNT(*) FROM Devices"));
            Assert.Equal(1, await ScalarAsync<long>(scopedConnectionString, "SELECT COUNT(*) FROM Loops"));
            Assert.Equal(0, await ScalarAsync<long>(scopedConnectionString, "SELECT COUNT(*) FROM People"));
            Assert.Equal(1, await ScalarAsync<long>(scopedConnectionString,
                "SELECT COUNT(*) FROM CloudStateImportRejections"));
            Assert.Equal("missing-parent:Accounts/AccountId=missing-account",
                await ScalarAsync<string>(scopedConnectionString,
                    "SELECT Reason FROM CloudStateImportRejections WHERE EntityType='Person' AND EntityKey='orphan-person'"));
            Assert.Equal("orphan-person", await ScalarAsync<string>(scopedConnectionString,
                "SELECT Payload->>'PersonId' FROM CloudStateImportRejections WHERE EntityType='Person'"));
            Assert.Equal("existing-user", await ScalarAsync<string>(scopedConnectionString,
                "SELECT UserId FROM Users WHERE LOWER(Email)='existing@example.com'"));
            Assert.Equal(PostgreSqlCloudStateSnapshotImporter.Sha256(token),
                await ScalarAsync<string>(scopedConnectionString, "SELECT TokenHash FROM CloudAuthTokens"));
            Assert.Equal("device-1", await ScalarAsync<string>(scopedConnectionString,
                "SELECT DeviceId FROM CloudAuthTokens"));
            var metadata = JsonDocument.Parse(await ScalarAsync<string>(scopedConnectionString,
                "SELECT Metadata::text FROM CloudAuthTokens"));
            Assert.False(metadata.RootElement.TryGetProperty("LastTranscript", out _));
            var audit = JsonDocument.Parse(await ScalarAsync<string>(scopedConnectionString,
                "SELECT Audit::text FROM RobotIdentityLinks"));
            Assert.Equal(JsonValueKind.Array, audit.RootElement.ValueKind);
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

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is T typed ? typed : (T)Convert.ChangeType(value!, typeof(T));
    }

    private static async Task ExecuteAdminAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class RecordingBackupPayloadStore : IBackupPayloadStore
    {
        public Dictionary<string, byte[]> Payloads { get; } = new(StringComparer.Ordinal);
        public bool CorruptOnLoad { get; init; }

        public Task<string> StoreAsync(string key, byte[] payload, string sha256,
            CancellationToken cancellationToken = default)
        {
            Payloads[key] = payload.ToArray();
            return Task.FromResult(key);
        }

        public Task<byte[]?> LoadAsync(string uri, CancellationToken cancellationToken = default) =>
            Task.FromResult(Payloads.TryGetValue(uri, out var payload)
                ? CorruptOnLoad ? [.. payload, (byte)'!'] : payload.ToArray()
                : null);
    }
}
