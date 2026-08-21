using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Npgsql;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PostgreSqlAtomicLoopBackupRestorerIntegrationTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task FailedMidRestoreRollsBackAllLoopChangesAndManifestStatus()
    {
        const string variable = "OPENJIBO_TEST_POSTGRES_CONNECTION_STRING";
        var adminConnectionString = Environment.GetEnvironmentVariable(variable)
                                    ?? throw new InvalidOperationException($"Set {variable}.");
        var schema = $"openjibo_backup_restore_{Guid.NewGuid():N}";
        await ExecuteAsync(adminConnectionString, $"CREATE SCHEMA \"{schema}\"");
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema };
        var connectionString = builder.ConnectionString;

        try
        {
            await ApplyStateMigrationsAsync(connectionString);
            await ExecuteAsync(connectionString, """
                INSERT INTO Accounts(AccountId,Email,AccessKeyId,IsDefault)
                VALUES('account-1','owner@example.com','access-1',TRUE);
                INSERT INTO Loops(LoopId,Name,OwnerAccountId,PrimaryRobotId)
                VALUES('loop-1','Current loop','account-1','robot-1');
                INSERT INTO LoopMembers(MemberId,LoopId,Status,MemberType)
                VALUES('stale-member','loop-1','active','owner');
                INSERT INTO HolidayOverrides(HolidayId,EventId,Name,Category,LoopId,IsEnabled,EventDate,Source,CountryCode)
                VALUES('stale-holiday','','Stale','holiday','loop-1',TRUE,CURRENT_DATE,'test','US');
                INSERT INTO BackupManifests(BackupId,AccountId,LoopId,Name,BlobUri,ContentSha256,
                    ContentLength,BackupSchemaVersion,Status)
                VALUES('backup-1','account-1','loop-1','test','memory:///backup',
                    '0000000000000000000000000000000000000000000000000000000000000000',0,2,'available');
                """);

            var snapshot = new RelationalLoopBackup(
                new LoopRecord
                {
                    LoopId = "loop-1", Name = "Restored loop", OwnerAccountId = "account-1",
                    RobotId = "robot-1"
                }, [],
                [new LoopMemberRecord { Id = "member-from-backup", LoopId = "loop-1" }],
                [],
                [new HolidayRecord { Id = "holiday-from-backup", LoopId = "loop-1" }],
                [new CommuteProfileRecord
                {
                    Id = "invalid-commute", LoopId = "loop-1", WorkHour = 99
                }],
                [], [], [], null, [], []);
            var manifest = new BackupManifestRecord("backup-1", "account-1", "loop-1", "test",
                "memory:///backup", new string('0', 64), 0, 2, "available", DateTimeOffset.UtcNow);

            await using var dataSource = new PostgreSqlCloudStateDataSource(connectionString, maxPoolSize: 2);
            var restorer = new PostgreSqlAtomicLoopBackupRestorer(dataSource);
            await Assert.ThrowsAsync<PostgresException>(() => restorer.RestoreAsync(
                "account-1", manifest, snapshot, DateTimeOffset.UtcNow));

            Assert.Equal("Current loop", await ScalarAsync<string>(connectionString,
                "SELECT Name FROM Loops WHERE LoopId='loop-1'"));
            Assert.Equal(1, await ScalarAsync<long>(connectionString,
                "SELECT COUNT(*) FROM LoopMembers WHERE LoopId='loop-1'"));
            Assert.Equal("stale-member", await ScalarAsync<string>(connectionString,
                "SELECT MemberId FROM LoopMembers WHERE LoopId='loop-1'"));
            Assert.Equal(1, await ScalarAsync<long>(connectionString,
                "SELECT COUNT(*) FROM HolidayOverrides WHERE LoopId='loop-1'"));
            Assert.Equal("available", await ScalarAsync<string>(connectionString,
                "SELECT Status FROM BackupManifests WHERE BackupId='backup-1'"));
            Assert.Equal(0, await ScalarAsync<long>(connectionString,
                "SELECT Revision FROM CloudStateMetadata WHERE StateKey='cloud-state'"));
        }
        finally
        {
            await ExecuteAsync(adminConnectionString, $"DROP SCHEMA \"{schema}\" CASCADE");
        }
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task SuccessfulRestoreReplacesScopedRowsAndPreservesOtherLoops()
    {
        const string variable = "OPENJIBO_TEST_POSTGRES_CONNECTION_STRING";
        var adminConnectionString = Environment.GetEnvironmentVariable(variable)
                                    ?? throw new InvalidOperationException($"Set {variable}.");
        var schema = $"openjibo_backup_replace_{Guid.NewGuid():N}";
        await ExecuteAsync(adminConnectionString, $"CREATE SCHEMA \"{schema}\"");
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema };
        var connectionString = builder.ConnectionString;

        try
        {
            await ApplyStateMigrationsAsync(connectionString);
            await ExecuteAsync(connectionString, """
                INSERT INTO Accounts(AccountId,Email,AccessKeyId,IsDefault) VALUES
                    ('account-1','owner@example.com','access-1',TRUE),
                    ('account-2','other@example.com','access-2',FALSE);
                INSERT INTO Loops(LoopId,Name,OwnerAccountId,PrimaryRobotId) VALUES
                    ('loop-1','Current loop','account-1','robot-1'),
                    ('loop-2','Other loop','account-2','robot-2');
                INSERT INTO LoopMembers(MemberId,LoopId,Status,MemberType) VALUES
                    ('stale-member','loop-1','active','owner'),
                    ('other-member','loop-2','active','owner');
                INSERT INTO BackupManifests(BackupId,AccountId,LoopId,Name,BlobUri,ContentSha256,
                    ContentLength,BackupSchemaVersion,Status)
                VALUES('backup-1','account-1','loop-1','test','memory:///backup',
                    '0000000000000000000000000000000000000000000000000000000000000000',0,2,'available');
                """);

            var member = new LoopMemberRecord { Id = "restored-member", LoopId = "loop-1" };
            var snapshot = new RelationalLoopBackup(
                new LoopRecord
                {
                    LoopId = "loop-1", Name = "Restored loop", OwnerAccountId = "account-1",
                    RobotId = "robot-1"
                }, [],
                [member],
                [new PersonRecord
                {
                    PersonId = "restored-person", AccountId = "account-1", LoopId = "loop-1",
                    RobotId = "robot-1", DisplayName = "Restored Person"
                }],
                [], [], [], [],
                [new RecognitionObservationRecord
                {
                    ObservationId = "restored-observation", LoopId = "loop-1", MemberId = member.Id,
                    RobotId = "robot-1", Modality = "face", Confidence = .9
                }],
                new LoopSymmetricKeyRecord("loop-1", [1, 2, 3], "wrap-1", "AES-256-GCM",
                    DateTimeOffset.UtcNow),
                [new StoredKeyRequest(new KeyRequestRecord
                {
                    RequestId = "request-1", LoopId = "loop-1", PublicKey = "public"
                })],
                [new StoredMediaRecord(new MediaRecord
                {
                    Path = "/media/restored", AccountId = "account-1", LoopId = "loop-1",
                    MediaType = "image", Reference = "camera", Url = "blob:///restored"
                })]);
            var manifest = new BackupManifestRecord("backup-1", "account-1", "loop-1", "test",
                "memory:///backup", new string('0', 64), 0, 2, "available", DateTimeOffset.UtcNow);

            await using var dataSource = new PostgreSqlCloudStateDataSource(connectionString, maxPoolSize: 2);
            await new PostgreSqlAtomicLoopBackupRestorer(dataSource).RestoreAsync(
                "account-1", manifest, snapshot, DateTimeOffset.UtcNow);

            Assert.Equal("Restored loop", await ScalarAsync<string>(connectionString,
                "SELECT Name FROM Loops WHERE LoopId='loop-1'"));
            Assert.Equal("restored-member", await ScalarAsync<string>(connectionString,
                "SELECT MemberId FROM LoopMembers WHERE LoopId='loop-1'"));
            Assert.Equal("other-member", await ScalarAsync<string>(connectionString,
                "SELECT MemberId FROM LoopMembers WHERE LoopId='loop-2'"));
            Assert.Equal(1, await ScalarAsync<long>(connectionString,
                "SELECT COUNT(*) FROM RecognitionObservations WHERE LoopId='loop-1'"));
            Assert.Equal(1, await ScalarAsync<long>(connectionString,
                "SELECT COUNT(*) FROM LoopSymmetricKeys WHERE LoopId='loop-1'"));
            Assert.Equal(1, await ScalarAsync<long>(connectionString,
                "SELECT COUNT(*) FROM KeyRequests WHERE LoopId='loop-1'"));
            Assert.Equal(1, await ScalarAsync<long>(connectionString,
                "SELECT COUNT(*) FROM MediaRecords WHERE LoopId='loop-1'"));
            Assert.Equal("restored", await ScalarAsync<string>(connectionString,
                "SELECT Status FROM BackupManifests WHERE BackupId='backup-1'"));
            Assert.Equal(1, await ScalarAsync<long>(connectionString,
                "SELECT Revision FROM CloudStateMetadata WHERE StateKey='cloud-state'"));
        }
        finally
        {
            await ExecuteAsync(adminConnectionString, $"DROP SCHEMA \"{schema}\" CASCADE");
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

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
