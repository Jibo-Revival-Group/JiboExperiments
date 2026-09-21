using System.Diagnostics;
using System.Security.Cryptography;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Persistence;
using Npgsql;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PostgreSqlAwsSigV4ReplayObservationStoreTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task ObserveAsync_AtomicallyClassifiesConcurrentReplicasAndResetsExpiredDigest()
    {
        await using var database = await ReplayTestDatabase.CreateAsync();
        await using var firstSource = new PostgreSqlCloudStateDataSource(database.ConnectionString, 2);
        await using var secondSource = new PostgreSqlCloudStateDataSource(database.ConnectionString, 2);
        var firstStore = new PostgreSqlAwsSigV4ReplayObservationStore(firstSource);
        var secondStore = new PostgreSqlAwsSigV4ReplayObservationStore(secondSource);
        var digest = SHA256.HashData("opaque-replay-sentinel"u8);

        var observations = await Task.WhenAll(Enumerable.Range(0, 32).Select(index =>
            (index & 1) == 0
                ? firstStore.ObserveAsync(digest, 1, "Account.CreateHubToken")
                : secondStore.ObserveAsync(digest, 1, "Account.CreateHubToken")));

        Assert.Single(observations, observation =>
            observation.Status == AwsSigV4ReplayObservationStatus.FirstSeen);
        Assert.Equal(31, observations.Count(observation =>
            observation.Status == AwsSigV4ReplayObservationStatus.Repeat));
        Assert.Equal(32, await database.ExecuteScalarAsync<long>(
            "SELECT ObservationCount FROM AwsSigV4ReplayObservations"));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM AwsSigV4ReplayObservations"));

        var preExpiryRotation = await firstStore.ObserveAsync(
            digest, 2, "Notification.NewRobotToken");
        Assert.Equal(AwsSigV4ReplayObservationStatus.Repeat, preExpiryRotation.Status);
        Assert.Equal(33, preExpiryRotation.ObservationCount);
        Assert.Equal(1, await database.ExecuteScalarAsync<short>(
            "SELECT KeyVersion FROM AwsSigV4ReplayObservations"));
        Assert.Equal("Account.CreateHubToken", await database.ExecuteScalarAsync<string>(
            "SELECT Operation FROM AwsSigV4ReplayObservations"));

        await database.ExecuteAsync(
            "UPDATE AwsSigV4ReplayObservations SET ExpiresUtc = clock_timestamp() - INTERVAL '1 second'");
        var reset = await firstStore.ObserveAsync(digest, 2, "Notification.NewRobotToken");

        Assert.Equal(AwsSigV4ReplayObservationStatus.FirstSeen, reset.Status);
        Assert.Equal(1, reset.ObservationCount);
        Assert.Equal(2, await database.ExecuteScalarAsync<short>(
            "SELECT KeyVersion FROM AwsSigV4ReplayObservations"));
        Assert.Equal("Notification.NewRobotToken", await database.ExecuteScalarAsync<string>(
            "SELECT Operation FROM AwsSigV4ReplayObservations"));
    }

    [Fact]
    public async Task ObserveAsync_RejectsInvalidMaterialBeforeOpeningDatabase()
    {
        await using var source = new PostgreSqlCloudStateDataSource(
            "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1");
        var store = new PostgreSqlAwsSigV4ReplayObservationStore(source);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ObserveAsync(new byte[31], 1, "Account.CreateHubToken"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.ObserveAsync(new byte[32], 0, "Account.CreateHubToken"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ObserveAsync(new byte[32], 1, " "));
    }

    private sealed class ReplayTestDatabase : IAsyncDisposable
    {
        private const string ConnectionVariable = "OPENJIBO_TEST_POSTGRES_CONNECTION_STRING";
        private readonly string _adminConnectionString;
        private readonly string _schemaName;

        private ReplayTestDatabase(string adminConnectionString, string schemaName)
        {
            _adminConnectionString = adminConnectionString;
            _schemaName = schemaName;
            ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                SearchPath = schemaName,
                ApplicationName = "OpenJibo.SigV4Replay.IntegrationTests",
                MaxPoolSize = 4
            }.ConnectionString;
        }

        internal string ConnectionString { get; }

        internal static async Task<ReplayTestDatabase> CreateAsync()
        {
            var admin = Environment.GetEnvironmentVariable(ConnectionVariable)
                        ?? throw new InvalidOperationException($"Set {ConnectionVariable}.");
            var schema = $"openjibo_sigv4_replay_test_{Guid.NewGuid():N}";
            var database = new ReplayTestDatabase(admin, schema);
            await using (var connection = new NpgsqlConnection(admin))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE SCHEMA {QuoteIdentifier(schema)}";
                await command.ExecuteNonQueryAsync();
            }

            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql",
                    "013_create_sigv4_replay_observations.state.sql");
                await database.ExecuteAsync(await File.ReadAllTextAsync(path));
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
            Debug.Assert(identifier.StartsWith("openjibo_sigv4_replay_test_", StringComparison.Ordinal));
            return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }
    }
}
