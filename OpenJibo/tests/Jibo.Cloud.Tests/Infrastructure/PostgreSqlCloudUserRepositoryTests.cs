using System.Security.Cryptography;
using System.Text;
using Jibo.Cloud.Infrastructure.Persistence;
using Npgsql;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PostgreSqlCloudUserRepositoryTests
{
    [Fact]
    public void PasswordHasher_MatchesLegacySaltColonPasswordSha256Format()
    {
        const string salt = "00112233445566778899aabbccddeeff";
        const string password = "correct horse battery staple";
        var expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{password}"))).ToLowerInvariant();

        Assert.Equal(expected, CloudUserPasswordHasher.Hash(password, salt));
        Assert.True(CloudUserPasswordHasher.Verify(password, salt, expected));
        Assert.False(CloudUserPasswordHasher.Verify("wrong", salt, expected));
        Assert.Matches("^[0-9a-f]{32}$", CloudUserPasswordHasher.GenerateSalt());
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Repository_CreatesAuthenticatesUpdatesAndHandlesConcurrentDuplicateEmail()
    {
        const string variable = "OPENJIBO_TEST_POSTGRES_CONNECTION_STRING";
        var adminConnectionString = Environment.GetEnvironmentVariable(variable)
                                    ?? throw new InvalidOperationException($"Set {variable}.");
        var schema = $"openjibo_user_repo_{Guid.NewGuid():N}";
        await ExecuteAsync(adminConnectionString, $"CREATE SCHEMA \"{schema}\"");
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema };
        var connectionString = builder.ConnectionString;

        try
        {
            await ApplyStateMigrationsAsync(connectionString);
            await using (var dataSource = new PostgreSqlCloudStateDataSource(connectionString, maxPoolSize: 4))
            {
                var protector = new UserDataCloudStateSecretProtector(
                    new UserDataEncryptionService("user-repo-passphrase", "user-repo-salt"));
                var repository = new PostgreSqlCloudUserRepository(dataSource, protector);

                var created = await repository.CreateAsync(
                    "  Ada@Example.COM ", "secret-password", " Ada ", null);
                Assert.NotNull(created);
                Assert.Equal("ada@example.com", created.Email);
                Assert.Equal("Ada", created.FirstName);
                Assert.Equal(string.Empty, created.LastName);
                Assert.Null(created.Gender);
                Assert.Null(created.Birthday);
                Assert.NotEqual("secret-password", created.PasswordHash);
                Assert.StartsWith("sk-", created.SecretAccessKey, StringComparison.Ordinal);

                var byId = await repository.GetByIdAsync(created.Id.ToUpperInvariant());
                var byEmail = await repository.GetByEmailAsync("ADA@example.com");
                Assert.Equal(created.Id, byId?.Id);
                Assert.Equal(created.SecretAccessKey, byEmail?.SecretAccessKey);
                Assert.Equal(created.Id, (await repository.AuthenticateAsync(
                    "ADA@example.com", "secret-password"))?.Id);
                Assert.Null(await repository.AuthenticateAsync("ada@example.com", "wrong-password"));

                var updated = await repository.UpdateProfileAsync(
                    created.Id, null, " Lovelace ", "female", 18151210);
                Assert.Equal("Ada", updated.FirstName);
                Assert.Equal("Lovelace", updated.LastName);
                Assert.Equal("female", updated.Gender);
                Assert.Equal(18151210, updated.Birthday);
                Assert.Equal(created.PasswordHash, updated.PasswordHash);
                Assert.Equal(created.SecretAccessKey, updated.SecretAccessKey);

                Assert.Null(await repository.CreateAsync(
                    "ADA@EXAMPLE.COM", "another-password", null, null));

                var concurrent = await Task.WhenAll(
                    repository.CreateAsync("race@example.com", "password-a", null, null),
                    repository.CreateAsync("RACE@example.com", "password-b", null, null));
                Assert.Single(concurrent, user => user is not null);

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    repository.UpdateProfileAsync("missing-user", "Missing", null, null, null));
            }

            Assert.Equal(2, await ScalarAsync<long>(connectionString, "SELECT COUNT(*) FROM Users"));
            Assert.Equal(3, await ScalarAsync<long>(connectionString,
                "SELECT Revision FROM CloudStateMetadata WHERE StateKey='cloud-state'"));
            Assert.Equal(0, await ScalarAsync<long>(connectionString, """
                SELECT COUNT(*) FROM Users
                WHERE encode(SecretAccessKeyCiphertext, 'escape') LIKE '%sk-%'
                """));
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
