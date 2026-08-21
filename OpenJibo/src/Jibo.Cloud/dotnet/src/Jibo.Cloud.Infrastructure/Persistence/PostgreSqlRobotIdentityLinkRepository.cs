using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlRobotIdentityLinkRepository(PostgreSqlCloudStateDataSource dataSource)
    : IRobotIdentityLinkRepository
{
    public async Task<RobotIdentityLinkRecord?> FindAsync(string observedDeviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observedDeviceId)) return null;
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT ObservedDeviceId, InventoryDeviceId, ClaimSource, ClaimedUtc,
                                     UpdatedUtc, RevokedUtc, Audit
                              FROM RobotIdentityLinks
                              WHERE LOWER(ObservedDeviceId) = LOWER(@observedDeviceId)
                                AND RevokedUtc IS NULL
                              """;
        command.Parameters.AddWithValue("observedDeviceId", observedDeviceId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<RobotIdentityLinkRecord> UpsertAsync(string observedDeviceId, string inventoryDeviceId,
        string claimSource, IReadOnlyList<RobotIdentityLinkAuditEntry>? audit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observedDeviceId))
            throw new ArgumentException("Observed device ID is required.", nameof(observedDeviceId));
        if (string.IsNullOrWhiteSpace(inventoryDeviceId))
            throw new ArgumentException("Inventory device ID is required.", nameof(inventoryDeviceId));
        var observed = observedDeviceId.Trim();
        var inventory = inventoryDeviceId.Trim();
        var source = string.IsNullOrWhiteSpace(claimSource) ? "admin-claim" : claimSource.Trim();
        var auditEntries = audit?.ToArray() ??
                           [new RobotIdentityLinkAuditEntry("linked", null, inventory, source,
                               DateTimeOffset.UtcNow)];
        var auditJson = JsonSerializer.Serialize(auditEntries);

        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        RobotIdentityLinkRecord result;
        await using (var command = new NpgsqlCommand("""
                                                     INSERT INTO RobotIdentityLinks
                                                         (ObservedDeviceId, InventoryDeviceId, ClaimSource, Audit)
                                                     VALUES (@observed, @inventory, @source, @audit)
                                                     ON CONFLICT ((LOWER(ObservedDeviceId))) DO UPDATE SET
                                                         InventoryDeviceId = EXCLUDED.InventoryDeviceId,
                                                         ClaimSource = EXCLUDED.ClaimSource,
                                                         UpdatedUtc = NOW(),
                                                         RevokedUtc = NULL,
                                                         Audit = RobotIdentityLinks.Audit || EXCLUDED.Audit
                                                     RETURNING ObservedDeviceId, InventoryDeviceId, ClaimSource,
                                                               ClaimedUtc, UpdatedUtc, RevokedUtc, Audit
                                                     """, connection, transaction))
        {
            command.Parameters.AddWithValue("observed", observed);
            command.Parameters.AddWithValue("inventory", inventory);
            command.Parameters.AddWithValue("source", source);
            command.Parameters.AddWithValue("audit", NpgsqlDbType.Jsonb, auditJson);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Robot identity link could not be saved.");
            result = Map(reader);
        }

        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<bool> RevokeAsync(string observedDeviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observedDeviceId)) return false;
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        int affected;
        await using (var command = new NpgsqlCommand("""
                                                     UPDATE RobotIdentityLinks
                                                     SET RevokedUtc = NOW(), UpdatedUtc = NOW()
                                                     WHERE LOWER(ObservedDeviceId) = LOWER(@observedDeviceId)
                                                       AND RevokedUtc IS NULL
                                                     """, connection, transaction))
        {
            command.Parameters.AddWithValue("observedDeviceId", observedDeviceId.Trim());
            affected = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (affected > 0) await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<IReadOnlyList<RobotIdentityLinkRecord>> ListForAccountAsync(string accountId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return [];
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT r.ObservedDeviceId, r.InventoryDeviceId, r.ClaimSource, r.ClaimedUtc,
                                     r.UpdatedUtc, r.RevokedUtc, r.Audit
                              FROM RobotIdentityLinks r
                              INNER JOIN AccountDevices ad ON ad.DeviceId = r.InventoryDeviceId
                              WHERE ad.AccountId = @accountId AND r.RevokedUtc IS NULL
                              ORDER BY r.UpdatedUtc DESC
                              """;
        command.Parameters.AddWithValue("accountId", accountId.Trim());
        var result = new List<RobotIdentityLinkRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader));
        return result;
    }

    private static RobotIdentityLinkRecord Map(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetFieldValue<DateTimeOffset>(3),
        reader.GetFieldValue<DateTimeOffset>(4),
        reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
        DeserializeAudit(reader.GetString(6)));

    private static IReadOnlyList<RobotIdentityLinkAuditEntry> DeserializeAudit(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<RobotIdentityLinkAuditEntry[]>(json) ?? []
            : [];
    }
}
