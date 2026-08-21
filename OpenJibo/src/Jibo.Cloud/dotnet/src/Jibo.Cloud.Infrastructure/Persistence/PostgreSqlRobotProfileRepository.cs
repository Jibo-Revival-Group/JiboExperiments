using System.Text.Json;
using Jibo.Cloud.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlRobotProfileRepository(PostgreSqlCloudStateDataSource dataSource)
    : IRobotProfileRepository
{
    public async Task<RobotProfile?> GetAsync(string robotId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(robotId)) return null;
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
                                                    SELECT RobotId, Payload, CalibrationPayload, CreatedUtc, UpdatedUtc
                                                    FROM RobotProfiles WHERE LOWER(RobotId) = LOWER(@robotId)
                                                    """, connection);
        command.Parameters.AddWithValue("robotId", robotId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<RobotProfile> UpsertAsync(RobotProfile profile, string? deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.RobotId);
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
                                                    INSERT INTO RobotProfiles
                                                        (RobotId, DeviceId, Payload, CalibrationPayload,
                                                         CreatedUtc, UpdatedUtc)
                                                    VALUES (@robotId, @deviceId, @payload, @calibration,
                                                            @created, @updated)
                                                    ON CONFLICT (RobotId) DO UPDATE SET
                                                        DeviceId = EXCLUDED.DeviceId,
                                                        Payload = EXCLUDED.Payload,
                                                        CalibrationPayload = EXCLUDED.CalibrationPayload,
                                                        UpdatedUtc = EXCLUDED.UpdatedUtc
                                                    """, connection, transaction);
        command.Parameters.AddWithValue("robotId", profile.RobotId.Trim());
        NpgsqlParameterHelpers.AddNullable(command.Parameters, "deviceId", NpgsqlDbType.Text,
            string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim());
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(profile.Payload));
        command.Parameters.AddWithValue("calibration", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(profile.CalibrationPayload));
        command.Parameters.AddWithValue("created", profile.CreatedUtc);
        command.Parameters.AddWithValue("updated", profile.UpdatedUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return profile;
    }

    private static RobotProfile Map(NpgsqlDataReader reader) => new()
    {
        RobotId = reader.GetString(0),
        Payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(reader.GetString(1)) ?? [],
        CalibrationPayload = JsonSerializer.Deserialize<Dictionary<string, object?>>(reader.GetString(2)) ?? [],
        CreatedUtc = reader.GetFieldValue<DateTimeOffset>(3),
        UpdatedUtc = reader.GetFieldValue<DateTimeOffset>(4)
    };
}
