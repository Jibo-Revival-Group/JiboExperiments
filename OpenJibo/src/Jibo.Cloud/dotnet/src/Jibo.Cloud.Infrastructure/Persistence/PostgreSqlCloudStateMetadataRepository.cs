using Npgsql;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal sealed class PostgreSqlCloudStateMetadataRepository(PostgreSqlCloudStateDataSource dataSource)
    : ICloudStateMetadataRepository
{
    public async Task<CloudStateMetadataRecord> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
                                                    SELECT SchemaVersion, Revision, UpdatedUtc
                                                    FROM CloudStateMetadata
                                                    WHERE StateKey = 'cloud-state'
                                                    """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "CloudStateMetadata is missing. Run the state migrations before starting the service.");
        return new CloudStateMetadataRecord(reader.GetInt32(0), reader.GetInt64(1),
            reader.GetFieldValue<DateTimeOffset>(2));
    }

    public async Task<bool> HasLegacySnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
                                                    SELECT EXISTS (
                                                        SELECT 1 FROM PersistenceSnapshots
                                                        WHERE SnapshotName = 'cloud-state'
                                                          AND LENGTH(SnapshotJson) > 2)
                                                    """, connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
}
