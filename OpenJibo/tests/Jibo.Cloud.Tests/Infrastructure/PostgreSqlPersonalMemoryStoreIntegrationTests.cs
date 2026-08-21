using System.Diagnostics;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Persistence;
using Npgsql;

namespace Jibo.Cloud.Tests.Infrastructure;

internal sealed class PostgreSqlIntegrationFactAttribute : FactAttribute
{
    public PostgreSqlIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("OPENJIBO_TEST_POSTGRES_CONNECTION_STRING")))
            Skip = "Set OPENJIBO_TEST_POSTGRES_CONNECTION_STRING to run PostgreSQL integration tests.";
    }
}

public sealed class PostgreSqlPersonalMemoryStoreIntegrationTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task RelationalStore_RoundTripsEveryMemoryKindAndIsolatesTenantScopes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var firstScope = new PersonalMemoryTenantScope("account-a", "loop-a", "device-a", "person-a");
        var secondScope = new PersonalMemoryTenantScope("account-a", "loop-a", "device-a", "person-b");

        using (var writer = new PostgreSqlPersonalMemoryStore(database.ConnectionString, cacheMaxEntries: 2))
        {
            writer.SetName(firstScope, "Ada");
            writer.SetBirthday(firstScope, "December 10");
            writer.SetPreference(firstScope, "Favorite Color", "blue");
            writer.SetImportantDate(firstScope, "Anniversary", "June 1");
            writer.SetAffinity(firstScope, "Robots", PersonalAffinity.Love);
            writer.AddListItem(firstScope, "Shopping", "Tea");
            writer.AddListItem(firstScope, "Shopping", "tea");

            writer.SetName(secondScope, "Grace");
            writer.SetPreference(secondScope, "Favorite Color", "green");
            writer.AddListItem(secondScope, "Shopping", "Coffee");
        }

        using var reader = new PostgreSqlPersonalMemoryStore(database.ConnectionString, cacheMaxEntries: 2);
        Assert.Equal("Ada", reader.GetName(firstScope));
        Assert.Equal("December 10", reader.GetBirthday(firstScope));
        Assert.Equal("blue", reader.GetPreference(firstScope, "favorite color"));
        Assert.Equal("June 1", reader.GetImportantDate(firstScope, "anniversary"));
        Assert.Equal(PersonalAffinity.Love, reader.GetAffinity(firstScope, "robots"));
        Assert.Equal(["Tea"], reader.GetListItems(firstScope, "shopping"));

        Assert.Equal("Grace", reader.GetName(secondScope));
        Assert.Null(reader.GetBirthday(secondScope));
        Assert.Equal("green", reader.GetPreference(secondScope, "favorite color"));
        Assert.Null(reader.GetImportantDate(secondScope, "anniversary"));
        Assert.Null(reader.GetAffinity(secondScope, "robots"));
        Assert.Equal(["Coffee"], reader.GetListItems(secondScope, "shopping"));

        Assert.Equal(2, await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM PersonalMemoryScopes"));
        Assert.Equal(2, await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM PersonalMemoryListItems"));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task LegacySnapshot_IsImportedOnceAndSubsequentConstructionIsIdempotent()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var legacySnapshot = JsonSerializer.Serialize(new
        {
            SchemaVersion = "1",
            Revision = 17,
            Tenants = new object[]
            {
                new
                {
                    TenantKey = "legacy-account|legacy-loop|legacy-device|legacy-person",
                    Birthday = "April 9",
                    Name = "Legacy Ada",
                    Preferences = new Dictionary<string, string> { ["Favorite Color"] = "purple" },
                    ImportantDates = new Dictionary<string, string> { ["Launch Day"] = "August 20" },
                    Affinities = new Dictionary<string, int> { ["Robots"] = (int)PersonalAffinity.Love },
                    Lists = new Dictionary<string, string[]> { ["Shopping"] = ["Tea", "Biscuits"] }
                },
                new
                {
                    TenantKey = "legacy-account|legacy-loop|other-device",
                    Birthday = (string?)null,
                    Name = "Other Device",
                    Preferences = new Dictionary<string, string>(),
                    ImportantDates = new Dictionary<string, string>(),
                    Affinities = new Dictionary<string, int>(),
                    Lists = new Dictionary<string, string[]>()
                }
            }
        });
        await database.InsertLegacySnapshotAsync(legacySnapshot);

        var scope = new PersonalMemoryTenantScope(
            "legacy-account", "legacy-loop", "legacy-device", "legacy-person");
        using (var first = new PostgreSqlPersonalMemoryStore(database.ConnectionString))
        {
            Assert.Equal("Legacy Ada", first.GetName(scope));
            Assert.Equal("April 9", first.GetBirthday(scope));
            Assert.Equal("purple", first.GetPreference(scope, "favorite color"));
            Assert.Equal("August 20", first.GetImportantDate(scope, "launch day"));
            Assert.Equal(PersonalAffinity.Love, first.GetAffinity(scope, "robots"));
            Assert.Equal(["Tea", "Biscuits"], first.GetListItems(scope, "shopping"));
            Assert.Equal(17, first.GetPersistenceStateInfo().Revision);
        }

        var changedSnapshot = legacySnapshot.Replace("Legacy Ada", "Must Not Reimport", StringComparison.Ordinal);
        await database.InsertLegacySnapshotAsync(changedSnapshot);

        using (var second = new PostgreSqlPersonalMemoryStore(database.ConnectionString))
        {
            Assert.Equal("Legacy Ada", second.GetName(scope));
        }

        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM PersonalMemoryImports WHERE ImportName = 'persistence-snapshot-v1'"));
        Assert.Equal(2, await database.ExecuteScalarAsync<long>("SELECT TenantCount FROM PersonalMemoryImports"));
        Assert.Equal(17, await database.ExecuteScalarAsync<long>("SELECT SourceRevision FROM PersonalMemoryImports"));
        Assert.Equal(2, await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM PersonalMemoryScopes"));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task PersonalMemoryMigration_IsNamedForItsTargetAndCanBeAppliedRepeatedly()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(applyMigrations: false);
        var scripts = PostgreSqlTestDatabase.GetMigrationScripts();
        var targetScript = Assert.Single(scripts,
            path => Path.GetFileName(path).EndsWith(".personal-memory.sql", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("003_normalize_personal_memory.personal-memory.sql", Path.GetFileName(targetScript));
        await database.ApplyMigrationsAsync();
        await database.ApplyMigrationsAsync();

        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM PersonalMemoryState WHERE StateKey = 'personal-memory'"));
    }

    private sealed class PostgreSqlTestDatabase : IAsyncDisposable
    {
        private const string ConnectionVariable = "OPENJIBO_TEST_POSTGRES_CONNECTION_STRING";
        private readonly string _adminConnectionString;
        private readonly string _schemaName;
        private bool _disposed;

        private PostgreSqlTestDatabase(string adminConnectionString, string schemaName)
        {
            _adminConnectionString = adminConnectionString;
            _schemaName = schemaName;
            var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                SearchPath = schemaName,
                ApplicationName = "OpenJibo.PersonalMemory.IntegrationTests"
            };
            ConnectionString = builder.ConnectionString;
        }

        public string ConnectionString { get; }

        public static async Task<PostgreSqlTestDatabase> CreateAsync(bool applyMigrations = true)
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException($"Set {ConnectionVariable} to run PostgreSQL integration tests.");

            var schemaName = $"openjibo_pm_test_{Guid.NewGuid():N}";
            var database = new PostgreSqlTestDatabase(connectionString, schemaName);
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE SCHEMA {QuoteIdentifier(schemaName)}";
                await command.ExecuteNonQueryAsync();
            }

            try
            {
                if (applyMigrations) await database.ApplyMigrationsAsync();
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public static string[] GetMigrationScripts()
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql");
            return Directory.GetFiles(directory, "*.sql", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public async Task ApplyMigrationsAsync()
        {
            var scripts = GetMigrationScripts()
                .Where(path => !Path.GetFileName(path).EndsWith(".state.sql", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            foreach (var path in scripts)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = await File.ReadAllTextAsync(path);
                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task InsertLegacySnapshotAsync(string json)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO PersistenceSnapshots (SnapshotName, SnapshotJson, UpdatedUtc)
                                  VALUES ('personal-memory', @json, NOW())
                                  ON CONFLICT (SnapshotName) DO UPDATE SET
                                      SnapshotJson = EXCLUDED.SnapshotJson,
                                      UpdatedUtc = EXCLUDED.UpdatedUtc
                                  """;
            command.Parameters.AddWithValue("json", json);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync();
            if (result is null or DBNull)
                throw new InvalidOperationException("The PostgreSQL scalar query returned no value.");
            return (T)Convert.ChangeType(result, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA {QuoteIdentifier(_schemaName)} CASCADE";
            await command.ExecuteNonQueryAsync();
        }

        private static string QuoteIdentifier(string identifier)
        {
            Debug.Assert(identifier.StartsWith("openjibo_pm_test_", StringComparison.Ordinal));
            return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }
    }
}
