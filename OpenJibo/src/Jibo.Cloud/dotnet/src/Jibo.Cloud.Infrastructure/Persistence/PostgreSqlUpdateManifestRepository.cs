using System.Text.Json;
using Jibo.Cloud.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlUpdateManifestRepository(PostgreSqlCloudStateDataSource dataSource)
    : IUpdateManifestRepository
{
    private const string Columns = "UpdateId, CreatedUtc, FromVersion, ToVersion, Changes, Url, ShaHash, " +
                                   "ContentLength, Subsystem, Filter, Dependencies";

    public async Task<IReadOnlyList<StoredUpdateManifest>> ListAsync(string? subsystem = null, string? filter = null,
        CancellationToken cancellationToken = default)
    {
        var scope = Normalize(subsystem);
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT {Columns}
                               FROM UpdateManifests
                               WHERE (@subsystem IS NULL OR LOWER(Subsystem) = LOWER(@subsystem))
                                 AND (@filter IS NULL OR LOWER(Filter) = LOWER(@filter))
                               ORDER BY CreatedUtc DESC, UpdateId
                               """;
        command.Parameters.Add("subsystem", NpgsqlDbType.Text).Value = (object?)scope ?? DBNull.Value;
        command.Parameters.Add("filter", NpgsqlDbType.Text).Value = (object?)Normalize(filter) ?? DBNull.Value;
        var result = new List<StoredUpdateManifest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader));
        return result;
    }

    public async Task<StoredUpdateManifest?> GetAsync(string updateId,
        CancellationToken cancellationToken = default)
    {
        var id = Require(updateId, nameof(updateId));
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM UpdateManifests WHERE UpdateId = @id";
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<StoredUpdateManifest> UpsertAsync(StoredUpdateManifest update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(update.Manifest);
        var manifest = update.Manifest;
        Require(manifest.UpdateId, nameof(manifest.UpdateId));
        Require(manifest.Subsystem, nameof(manifest.Subsystem));
        if (manifest.Length < 0) throw new ArgumentOutOfRangeException(nameof(manifest.Length));

        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO UpdateManifests
                (UpdateId, CreatedUtc, FromVersion, ToVersion, Changes, Url, ShaHash,
                 ContentLength, Subsystem, Filter, Dependencies)
            VALUES
                (@id, @created, @from, @to, @changes, @url, @sha, @length, @subsystem, @filter, @dependencies)
            ON CONFLICT (UpdateId) DO UPDATE SET
                CreatedUtc = EXCLUDED.CreatedUtc, FromVersion = EXCLUDED.FromVersion,
                ToVersion = EXCLUDED.ToVersion, Changes = EXCLUDED.Changes, Url = EXCLUDED.Url,
                ShaHash = EXCLUDED.ShaHash, ContentLength = EXCLUDED.ContentLength,
                Subsystem = EXCLUDED.Subsystem, Filter = EXCLUDED.Filter,
                Dependencies = EXCLUDED.Dependencies
            """, connection, transaction);
        command.Parameters.AddWithValue("id", manifest.UpdateId.Trim());
        command.Parameters.AddWithValue("created", manifest.CreatedUtc);
        command.Parameters.AddWithValue("from", manifest.FromVersion);
        command.Parameters.AddWithValue("to", manifest.ToVersion);
        command.Parameters.AddWithValue("changes", manifest.Changes);
        command.Parameters.AddWithValue("url", manifest.Url);
        command.Parameters.AddWithValue("sha", manifest.ShaHash);
        command.Parameters.AddWithValue("length", manifest.Length);
        command.Parameters.AddWithValue("subsystem", manifest.Subsystem.Trim());
        command.Parameters.Add("filter", NpgsqlDbType.Text).Value =
            (object?)Normalize(manifest.Filter) ?? DBNull.Value;
        command.Parameters.Add("dependencies", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(update.Dependencies);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return update;
    }

    public async Task<StoredUpdateManifest?> DeleteAsync(string updateId,
        CancellationToken cancellationToken = default)
    {
        var id = Require(updateId, nameof(updateId));
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"DELETE FROM UpdateManifests WHERE UpdateId = @id RETURNING {Columns}", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        StoredUpdateManifest? existing;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            existing = await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
        if (existing is not null) await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return existing;
    }

    private static StoredUpdateManifest Map(NpgsqlDataReader reader) => new(
        new UpdateManifest
        {
            UpdateId = reader.GetString(0),
            CreatedUtc = reader.GetFieldValue<DateTimeOffset>(1),
            FromVersion = reader.GetString(2),
            ToVersion = reader.GetString(3),
            Changes = reader.GetString(4),
            Url = reader.GetString(5),
            ShaHash = reader.GetString(6),
            Length = reader.GetInt64(7),
            Subsystem = reader.GetString(8),
            Filter = reader.IsDBNull(9) ? null : reader.GetString(9)
        }, DeserializeDictionary(reader.GetString(10)));

    private static IReadOnlyDictionary<string, object?> DeserializeDictionary(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new Dictionary<string, object?>();
    private static string Require(string value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("Value is required.", name);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
