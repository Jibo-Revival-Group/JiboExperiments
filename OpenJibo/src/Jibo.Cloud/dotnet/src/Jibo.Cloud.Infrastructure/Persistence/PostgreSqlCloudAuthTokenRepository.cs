using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlCloudAuthTokenRepository(PostgreSqlCloudStateDataSource dataSource)
    : ICloudAuthTokenRepository
{
    private static readonly HashSet<string> SupportedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "hub", "robot", "access"
    };

    public async Task<CloudAuthTokenRecord> IssueAsync(string token, string tokenKind, string? accountId,
        string? deviceId, DateTimeOffset expiresUtc, IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedKind = tokenKind?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!SupportedKinds.Contains(normalizedKind))
            throw new ArgumentException("Token kind must be hub, robot, or access.", nameof(tokenKind));
        if (expiresUtc <= DateTimeOffset.UtcNow)
            throw new ArgumentOutOfRangeException(nameof(expiresUtc), "Token expiry must be in the future.");

        var tokenHash = CloudAuthTokenHasher.Hash(token);
        var metadataJson = JsonSerializer.Serialize(metadata ?? new Dictionary<string, object?>());
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var issuedUtc = DateTimeOffset.UtcNow;
        await using (var command = new NpgsqlCommand("""
                                                     INSERT INTO CloudAuthTokens
                                                         (TokenHash, TokenKind, AccountId, DeviceId, IssuedUtc,
                                                          ExpiresUtc, Metadata)
                                                     VALUES
                                                         (@tokenHash, @tokenKind, @accountId, @deviceId, @issuedUtc,
                                                          @expiresUtc, @metadata)
                                                     ON CONFLICT (TokenHash) DO NOTHING
                                                     """, connection, transaction))
        {
            command.Parameters.AddWithValue("tokenHash", tokenHash);
            command.Parameters.AddWithValue("tokenKind", normalizedKind);
            NpgsqlParameterHelpers.AddNullable(command.Parameters, "accountId", NpgsqlDbType.Text,
                NormalizeNullable(accountId));
            NpgsqlParameterHelpers.AddNullable(command.Parameters, "deviceId", NpgsqlDbType.Text,
                NormalizeNullable(deviceId));
            command.Parameters.AddWithValue("issuedUtc", issuedUtc);
            command.Parameters.AddWithValue("expiresUtc", expiresUtc);
            command.Parameters.AddWithValue("metadata", NpgsqlDbType.Jsonb, metadataJson);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                throw new InvalidOperationException("The token has already been issued.");
        }

        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CloudAuthTokenRecord(normalizedKind, NormalizeNullable(accountId), NormalizeNullable(deviceId),
            issuedUtc, expiresUtc, null, DeserializeMetadata(metadataJson));
    }

    public async Task<CloudAuthTokenRecord?> FindValidAsync(string token, DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = CloudAuthTokenHasher.Hash(token);
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT TokenKind, AccountId, DeviceId, IssuedUtc, ExpiresUtc, RevokedUtc, Metadata
                              FROM CloudAuthTokens
                              WHERE TokenHash = @tokenHash
                                AND RevokedUtc IS NULL
                                AND ExpiresUtc > @now
                              """;
        command.Parameters.AddWithValue("tokenHash", tokenHash);
        command.Parameters.AddWithValue("now", now ?? DateTimeOffset.UtcNow);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<bool> RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        var tokenHash = CloudAuthTokenHasher.Hash(token);
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        int affected;
        await using (var command = new NpgsqlCommand("""
                                                     UPDATE CloudAuthTokens
                                                     SET RevokedUtc = NOW()
                                                     WHERE TokenHash = @tokenHash AND RevokedUtc IS NULL
                                                     """, connection, transaction))
        {
            command.Parameters.AddWithValue("tokenHash", tokenHash);
            affected = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (affected > 0) await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<int> RevokeForAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return 0;
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
                                                    UPDATE CloudAuthTokens SET RevokedUtc = NOW()
                                                    WHERE AccountId = @accountId AND RevokedUtc IS NULL
                                                    """, connection, transaction);
        command.Parameters.AddWithValue("accountId", accountId.Trim());
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected > 0) await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return affected;
    }

    private static CloudAuthTokenRecord Map(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetFieldValue<DateTimeOffset>(3),
        reader.GetFieldValue<DateTimeOffset>(4),
        reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
        DeserializeMetadata(reader.GetString(6)));

    private static IReadOnlyDictionary<string, object?> DeserializeMetadata(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ??
        new Dictionary<string, object?>();

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
