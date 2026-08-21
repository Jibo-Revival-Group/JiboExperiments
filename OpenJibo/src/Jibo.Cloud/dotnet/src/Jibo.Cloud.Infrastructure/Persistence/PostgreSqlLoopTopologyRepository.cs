using Jibo.Cloud.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlLoopTopologyRepository(PostgreSqlCloudStateDataSource dataSource)
    : ILoopTopologyRepository
{
    private const string LoopColumns = "LoopId, Name, OwnerAccountId, PrimaryRobotId, PrimaryRobotFriendlyId, " +
                                       "IsSuspended, CreatedUtc, UpdatedUtc";

    public Task<IReadOnlyList<StoredLoopTopology>> ListForAccountAsync(string accountId, int limit = 100,
        CancellationToken cancellationToken = default) =>
        ListAsync(accountId, null, limit, cancellationToken);

    public Task<IReadOnlyList<StoredLoopTopology>> ListForDeviceAsync(string accountId, string deviceId,
        int limit = 100, CancellationToken cancellationToken = default) =>
        ListAsync(accountId, Require(deviceId, nameof(deviceId)), limit, cancellationToken);

    public async Task<StoredLoopTopology?> GetAsync(string accountId, string loopId,
        CancellationToken cancellationToken = default)
    {
        var account = Require(accountId, nameof(accountId)); var loop = Require(loopId, nameof(loopId));
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {LoopColumns} FROM Loops WHERE OwnerAccountId = @account AND LoopId = @loop";
        command.Parameters.AddWithValue("account", account); command.Parameters.AddWithValue("loop", loop);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var record = MapLoop(reader);
        await reader.DisposeAsync();
        return new StoredLoopTopology(record, await ReadDevicesAsync(connection, record.LoopId, cancellationToken));
    }

    public async Task<StoredLoopTopology> UpsertAsync(StoredLoopTopology topology,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topology); ArgumentNullException.ThrowIfNull(topology.Loop);
        var loop = topology.Loop; Require(loop.LoopId, nameof(loop.LoopId));
        Require(loop.OwnerAccountId, nameof(loop.OwnerAccountId));
        var devices = topology.Devices.GroupBy(item => item.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
        if (devices.Count(item => item.IsPrimary) > 1)
            throw new ArgumentException("A loop can have only one primary device.", nameof(topology));
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand("""
            INSERT INTO Loops (LoopId, Name, OwnerAccountId, PrimaryRobotId, PrimaryRobotFriendlyId,
                               IsSuspended, CreatedUtc, UpdatedUtc)
            VALUES (@id, @name, @owner, @robot, @friendly, @suspended, @created, @updated)
            ON CONFLICT (LoopId) DO UPDATE SET Name = EXCLUDED.Name,
                PrimaryRobotId = EXCLUDED.PrimaryRobotId,
                PrimaryRobotFriendlyId = EXCLUDED.PrimaryRobotFriendlyId,
                IsSuspended = EXCLUDED.IsSuspended, UpdatedUtc = EXCLUDED.UpdatedUtc
            WHERE Loops.OwnerAccountId = EXCLUDED.OwnerAccountId
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", loop.LoopId.Trim()); command.Parameters.AddWithValue("name", loop.Name);
            command.Parameters.AddWithValue("owner", loop.OwnerAccountId.Trim());
            command.Parameters.Add("robot", NpgsqlDbType.Text).Value =
                (object?)Normalize(loop.RobotId) ?? DBNull.Value;
            command.Parameters.Add("friendly", NpgsqlDbType.Text).Value =
                (object?)Normalize(loop.RobotFriendlyId) ?? DBNull.Value;
            command.Parameters.AddWithValue("suspended", loop.IsSuspended);
            command.Parameters.AddWithValue("created", loop.CreatedUtc); command.Parameters.AddWithValue("updated", loop.UpdatedUtc);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                throw new InvalidOperationException("The loop belongs to another account.");
        }
        await using (var delete = new NpgsqlCommand("DELETE FROM LoopDevices WHERE LoopId = @loop", connection, transaction))
        { delete.Parameters.AddWithValue("loop", loop.LoopId.Trim()); await delete.ExecuteNonQueryAsync(cancellationToken); }
        foreach (var device in devices)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO LoopDevices (LoopId, DeviceId, IsPrimary, AddedUtc)
                VALUES (@loop, @device, @primary, @added)
                """, connection, transaction);
            insert.Parameters.AddWithValue("loop", loop.LoopId.Trim());
            insert.Parameters.AddWithValue("device", Require(device.DeviceId, nameof(device.DeviceId)));
            insert.Parameters.AddWithValue("primary", device.IsPrimary); insert.Parameters.AddWithValue("added", device.AddedUtc);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return topology;
    }

    public async Task<bool> DeleteAsync(string accountId, string loopId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM Loops WHERE OwnerAccountId = @account AND LoopId = @loop",
            connection, transaction);
        command.Parameters.AddWithValue("account", Require(accountId, nameof(accountId)));
        command.Parameters.AddWithValue("loop", Require(loopId, nameof(loopId)));
        var removed = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (removed) await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return removed;
    }

    private async Task<IReadOnlyList<StoredLoopTopology>> ListAsync(string accountId, string? deviceId, int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {LoopColumns} FROM Loops l
            WHERE l.OwnerAccountId = @account AND (@device IS NULL OR EXISTS
                (SELECT 1 FROM LoopDevices ld WHERE ld.LoopId = l.LoopId AND ld.DeviceId = @device))
            ORDER BY l.UpdatedUtc DESC, l.LoopId LIMIT @limit
            """;
        command.Parameters.AddWithValue("account", Require(accountId, nameof(accountId)));
        command.Parameters.Add("device", NpgsqlDbType.Text).Value = (object?)deviceId ?? DBNull.Value;
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
        var loops = new List<LoopRecord>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) loops.Add(MapLoop(reader));
        var result = new List<StoredLoopTopology>();
        foreach (var loop in loops)
            result.Add(new StoredLoopTopology(loop, await ReadDevicesAsync(connection, loop.LoopId, cancellationToken)));
        return result;
    }

    private static async Task<IReadOnlyList<LoopDeviceLink>> ReadDevicesAsync(NpgsqlConnection connection,
        string loopId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DeviceId, IsPrimary, AddedUtc FROM LoopDevices WHERE LoopId = @loop ORDER BY IsPrimary DESC, DeviceId";
        command.Parameters.AddWithValue("loop", loopId); var result = new List<LoopDeviceLink>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new LoopDeviceLink(reader.GetString(0),
            reader.GetBoolean(1), reader.GetFieldValue<DateTimeOffset>(2)));
        return result;
    }

    private static LoopRecord MapLoop(NpgsqlDataReader reader) => new()
    {
        LoopId = reader.GetString(0),
        Name = reader.GetString(1),
        OwnerAccountId = reader.GetString(2),
        RobotId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
        RobotFriendlyId = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
        IsSuspended = reader.GetBoolean(5),
        CreatedUtc = reader.GetFieldValue<DateTimeOffset>(6),
        UpdatedUtc = reader.GetFieldValue<DateTimeOffset>(7)
    };
    private static string Require(string value, string name) => !string.IsNullOrWhiteSpace(value)
        ? value.Trim() : throw new ArgumentException("Value is required.", name);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
