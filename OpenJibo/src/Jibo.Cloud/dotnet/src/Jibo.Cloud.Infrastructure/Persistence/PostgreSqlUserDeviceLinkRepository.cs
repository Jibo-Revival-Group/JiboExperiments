using Jibo.Cloud.Domain.Models;
using Npgsql;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlUserDeviceLinkRepository(PostgreSqlCloudStateDataSource dataSource)
    : IUserDeviceLinkRepository
{
    public async Task<UserDeviceLink> LinkAsync(string userId, string deviceId, string claimSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var normalizedUserId = userId.Trim();
        var normalizedDeviceId = deviceId.Trim();
        var linkedUtc = DateTimeOffset.UtcNow;
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var delete = new NpgsqlCommand(
                         "DELETE FROM UserDevices WHERE DeviceId = @deviceId", connection, transaction))
        {
            delete.Parameters.AddWithValue("deviceId", normalizedDeviceId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = new NpgsqlCommand("""
                                                    INSERT INTO UserDevices
                                                        (UserId, DeviceId, ClaimSource, LinkedUtc)
                                                    VALUES (@userId, @deviceId, @claimSource, @linkedUtc)
                                                    """, connection, transaction))
        {
            insert.Parameters.AddWithValue("userId", normalizedUserId);
            insert.Parameters.AddWithValue("deviceId", normalizedDeviceId);
            insert.Parameters.AddWithValue("claimSource",
                string.IsNullOrWhiteSpace(claimSource) ? "portal-pairing" : claimSource.Trim());
            insert.Parameters.AddWithValue("linkedUtc", linkedUtc);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new UserDeviceLink(normalizedUserId, normalizedDeviceId,
            string.IsNullOrWhiteSpace(claimSource) ? "portal-pairing" : claimSource.Trim(), linkedUtc);
    }

    public async Task<IReadOnlyList<string>> ListDeviceIdsForUserAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return [];

        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT DeviceId
                              FROM UserDevices
                              WHERE UserId = @userId
                              ORDER BY LinkedUtc, DeviceId
                              """;
        command.Parameters.AddWithValue("userId", userId.Trim());
        var deviceIds = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) deviceIds.Add(reader.GetString(0));
        return deviceIds;
    }

    public async Task<string?> FindUserIdByDeviceAsync(string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;

        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT UserId
                              FROM UserDevices
                              WHERE DeviceId = @deviceId
                              LIMIT 1
                              """;
        command.Parameters.AddWithValue("deviceId", deviceId.Trim());
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }
}
