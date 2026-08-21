using System.Security.Cryptography;
using System.Text;
using Jibo.Cloud.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlCloudUserRepository(
    PostgreSqlCloudStateDataSource dataSource,
    ICloudStateSecretProtector secretProtector) : ICloudUserRepository
{
    public async Task<UserRecord?> CreateAsync(string email, string password, string? firstName, string? lastName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return null;

        var salt = CloudUserPasswordHasher.GenerateSalt();
        var user = new UserRecord
        {
            Id = $"usr-{Guid.NewGuid():N}",
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = CloudUserPasswordHasher.Hash(password, salt),
            Salt = salt,
            FirstName = firstName?.Trim() ?? string.Empty,
            LastName = lastName?.Trim() ?? string.Empty,
            AccessKeyId = $"ak-{Guid.NewGuid():N}",
            SecretAccessKey = $"sk-{Guid.NewGuid():N}",
            CreatedUtc = DateTimeOffset.UtcNow
        };

        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var command = new NpgsqlCommand("""
                                                         INSERT INTO Users
                                                             (UserId, Email, PasswordHash, PasswordSalt,
                                                              FirstName, LastName, Gender, Birthday,
                                                              AccessKeyId, SecretAccessKeyCiphertext,
                                                              SecretWrappingKeyId, IsActive, CreatedUtc)
                                                         VALUES
                                                             (@id, @email, @passwordHash, @passwordSalt,
                                                              @firstName, @lastName, @gender, @birthday,
                                                              @accessKeyId, @secret, @keyId, @isActive, @createdUtc)
                                                         """, connection, transaction))
            {
                command.Parameters.AddWithValue("id", user.Id);
                command.Parameters.AddWithValue("email", user.Email);
                command.Parameters.AddWithValue("passwordHash", user.PasswordHash);
                command.Parameters.AddWithValue("passwordSalt", user.Salt);
                command.Parameters.AddWithValue("firstName", user.FirstName);
                command.Parameters.AddWithValue("lastName", user.LastName);
                NpgsqlParameterHelpers.AddNullable(command.Parameters, "gender", NpgsqlDbType.Text, user.Gender);
                NpgsqlParameterHelpers.AddNullable(command.Parameters, "birthday", NpgsqlDbType.Bigint,
                    user.Birthday);
                command.Parameters.AddWithValue("accessKeyId", user.AccessKeyId);
                command.Parameters.AddWithValue("secret", secretProtector.Protect(user.SecretAccessKey));
                command.Parameters.AddWithValue("keyId", secretProtector.KeyId);
                command.Parameters.AddWithValue("isActive", user.IsActive);
                command.Parameters.AddWithValue("createdUtc", user.CreatedUtc);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return user;
        }
        catch (PostgresException exception) when (IsEmailUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
    }

    public async Task<UserRecord?> AuthenticateAsync(string email, string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return null;
        var user = await GetByEmailAsync(email, cancellationToken);
        return user is not null && CloudUserPasswordHasher.Verify(password, user.Salt, user.PasswordHash)
            ? user
            : null;
    }

    public Task<UserRecord?> GetByIdAsync(string userId, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(userId)
            ? Task.FromResult<UserRecord?>(null)
            : ReadOneAsync("LOWER(UserId) = LOWER(@value)", userId.Trim(), cancellationToken);

    public Task<UserRecord?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(email)
            ? Task.FromResult<UserRecord?>(null)
            : ReadOneAsync("LOWER(Email) = LOWER(@value)", email.Trim(), cancellationToken);

    public async Task<UserRecord> UpdateProfileAsync(string userId, string? firstName, string? lastName,
        string? gender, long? birthday, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("A user ID is required.", nameof(userId));

        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string? resolvedId;
        await using (var command = new NpgsqlCommand("""
                                                     UPDATE Users
                                                     SET FirstName = COALESCE(@firstName, FirstName),
                                                         LastName = COALESCE(@lastName, LastName),
                                                         Gender = COALESCE(@gender, Gender),
                                                         Birthday = COALESCE(@birthday, Birthday),
                                                         UpdatedUtc = NOW()
                                                     WHERE LOWER(UserId) = LOWER(@userId)
                                                     RETURNING UserId
                                                     """, connection, transaction))
        {
            command.Parameters.AddWithValue("userId", userId.Trim());
            NpgsqlParameterHelpers.AddNullable(command.Parameters, "firstName", NpgsqlDbType.Text,
                firstName?.Trim());
            NpgsqlParameterHelpers.AddNullable(command.Parameters, "lastName", NpgsqlDbType.Text,
                lastName?.Trim());
            NpgsqlParameterHelpers.AddNullable(command.Parameters, "gender", NpgsqlDbType.Text, gender);
            NpgsqlParameterHelpers.AddNullable(command.Parameters, "birthday", NpgsqlDbType.Bigint, birthday);
            resolvedId = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }

        if (resolvedId is null)
            throw new InvalidOperationException($"User '{userId}' not found.");

        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetByIdAsync(resolvedId, cancellationToken)
               ?? throw new InvalidOperationException($"User '{resolvedId}' disappeared after update.");
    }

    private async Task<UserRecord?> ReadOneAsync(string predicate, string value,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT UserId, Email, PasswordHash, PasswordSalt, FirstName, LastName,
                                      Gender, Birthday, AccessKeyId, SecretAccessKeyCiphertext,
                                      SecretWrappingKeyId, IsActive, CreatedUtc
                               FROM Users
                               WHERE {predicate}
                               LIMIT 1
                               """;
        command.Parameters.AddWithValue("value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var ciphertext = reader.IsDBNull(9) ? null : reader.GetFieldValue<byte[]>(9);
        if (ciphertext is not null &&
            (reader.IsDBNull(10) ||
             !reader.GetString(10).Equals(secretProtector.KeyId, StringComparison.Ordinal)))
            throw new InvalidOperationException("The user secret uses an unavailable wrapping key.");

        return new UserRecord
        {
            Id = reader.GetString(0),
            Email = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            Salt = reader.GetString(3),
            FirstName = reader.GetString(4),
            LastName = reader.GetString(5),
            Gender = reader.IsDBNull(6) ? null : reader.GetString(6),
            Birthday = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            AccessKeyId = reader.GetString(8),
            SecretAccessKey = ciphertext is null ? string.Empty : secretProtector.Unprotect(ciphertext),
            IsActive = reader.GetBoolean(11),
            CreatedUtc = reader.GetFieldValue<DateTimeOffset>(12)
        };
    }

    private static bool IsEmailUniqueViolation(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation &&
        exception.ConstraintName?.Contains("users_email", StringComparison.OrdinalIgnoreCase) == true;
}

internal static class CloudUserPasswordHasher
{
    internal static string GenerateSalt()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string Hash(string password, string salt) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{password}"))).ToLowerInvariant();

    internal static bool Verify(string password, string salt, string expectedHash)
    {
        var actual = Encoding.ASCII.GetBytes(Hash(password, salt));
        var expected = Encoding.ASCII.GetBytes(expectedHash);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
