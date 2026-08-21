using System.Text.Json;
using Jibo.Cloud.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlMediaMetadataRepository(PostgreSqlCloudStateDataSource dataSource)
    : IMediaMetadataRepository
{
    private const string Columns = "MediaPath, CreatedUtc, MediaType, Reference, AccountId, LoopId, BlobUri, " +
                                   "ContentSha256, ContentLength, IsEncrypted, EncryptionKeyId, IsDeleted, " +
                                   "DeletedUtc, Meta";

    public async Task<IReadOnlyList<StoredMediaRecord>> ListAsync(string accountId,
        IReadOnlyList<string>? loopIds = null, DateTimeOffset? after = null, DateTimeOffset? before = null,
        int limit = 250, CancellationToken cancellationToken = default)
    {
        var account = Require(accountId, nameof(accountId));
        var loops = loopIds?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT {Columns}
                               FROM MediaRecords
                               WHERE AccountId = @accountId AND NOT IsDeleted
                                 AND (cardinality(@loopIds) = 0 OR LoopId = ANY(@loopIds))
                                 AND (@after IS NULL OR CreatedUtc > @after)
                                 AND (@before IS NULL OR CreatedUtc < @before)
                               ORDER BY CreatedUtc DESC, MediaPath
                               LIMIT @limit
                               """;
        command.Parameters.AddWithValue("accountId", account);
        command.Parameters.AddWithValue("loopIds", loops);
        command.Parameters.Add("after", NpgsqlDbType.TimestampTz).Value = (object?)after ?? DBNull.Value;
        command.Parameters.Add("before", NpgsqlDbType.TimestampTz).Value = (object?)before ?? DBNull.Value;
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000));
        return await ReadManyAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredMediaRecord>> GetAsync(string accountId, IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        var account = Require(accountId, nameof(accountId));
        var scopedPaths = NormalizePaths(paths);
        if (scopedPaths.Length == 0) return [];
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT {Columns}
                               FROM MediaRecords
                               WHERE AccountId = @accountId AND MediaPath = ANY(@paths)
                               ORDER BY CreatedUtc DESC, MediaPath
                               """;
        command.Parameters.AddWithValue("accountId", account);
        command.Parameters.AddWithValue("paths", scopedPaths);
        return await ReadManyAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredMediaRecord>> ListAllForBackupAsync(string accountId, string loopId,
        CancellationToken cancellationToken = default)
    {
        var account = Require(accountId, nameof(accountId));
        var loop = Require(loopId, nameof(loopId));
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT {Columns}
                               FROM MediaRecords
                               WHERE AccountId = @accountId AND LoopId = @loopId
                               ORDER BY CreatedUtc, MediaPath
                               """;
        command.Parameters.AddWithValue("accountId", account);
        command.Parameters.AddWithValue("loopId", loop);
        return await ReadManyAsync(command, cancellationToken);
    }

    public async Task<StoredMediaRecord> UpsertAsync(StoredMediaRecord media,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(media.Media);
        var item = media.Media;
        Require(item.Path, nameof(item.Path)); Require(item.AccountId, nameof(item.AccountId));
        Require(item.LoopId, nameof(item.LoopId)); Require(item.Url, nameof(item.Url));
        if (media.ContentLength < 0) throw new ArgumentOutOfRangeException(nameof(media.ContentLength));

        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO MediaRecords
                (MediaPath, CreatedUtc, MediaType, Reference, AccountId, LoopId, BlobUri,
                 ContentSha256, ContentLength, IsEncrypted, EncryptionKeyId, IsDeleted, DeletedUtc, Meta)
            VALUES
                (@path, @created, @type, @reference, @account, @loop, @uri,
                 @sha, @length, @encrypted, @keyId, @deleted, @deletedUtc, @meta)
            ON CONFLICT (MediaPath) DO UPDATE SET
                CreatedUtc = EXCLUDED.CreatedUtc, MediaType = EXCLUDED.MediaType,
                Reference = EXCLUDED.Reference, AccountId = EXCLUDED.AccountId, LoopId = EXCLUDED.LoopId,
                BlobUri = EXCLUDED.BlobUri, ContentSha256 = EXCLUDED.ContentSha256,
                ContentLength = EXCLUDED.ContentLength, IsEncrypted = EXCLUDED.IsEncrypted,
                EncryptionKeyId = EXCLUDED.EncryptionKeyId, IsDeleted = EXCLUDED.IsDeleted,
                DeletedUtc = EXCLUDED.DeletedUtc, Meta = EXCLUDED.Meta
            WHERE MediaRecords.AccountId = EXCLUDED.AccountId
            """, connection, transaction);
        command.Parameters.AddWithValue("path", item.Path.Trim());
        command.Parameters.AddWithValue("created", item.CreatedUtc);
        command.Parameters.AddWithValue("type", item.MediaType);
        command.Parameters.AddWithValue("reference", item.Reference);
        command.Parameters.AddWithValue("account", item.AccountId.Trim());
        command.Parameters.AddWithValue("loop", item.LoopId.Trim());
        command.Parameters.AddWithValue("uri", item.Url.Trim());
        command.Parameters.Add("sha", NpgsqlDbType.Text).Value =
            (object?)Normalize(media.ContentSha256) ?? DBNull.Value;
        command.Parameters.Add("length", NpgsqlDbType.Bigint).Value = (object?)media.ContentLength ?? DBNull.Value;
        command.Parameters.AddWithValue("encrypted", item.IsEncrypted);
        command.Parameters.Add("keyId", NpgsqlDbType.Text).Value =
            (object?)Normalize(media.EncryptionKeyId) ?? DBNull.Value;
        command.Parameters.AddWithValue("deleted", item.IsDeleted);
        command.Parameters.Add("deletedUtc", NpgsqlDbType.TimestampTz).Value =
            (object?)media.DeletedUtc ?? DBNull.Value;
        command.Parameters.Add("meta", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(item.Meta);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
            throw new InvalidOperationException("The media path belongs to another account.");
        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return media;
    }

    public async Task<IReadOnlyList<StoredMediaRecord>> SoftDeleteAsync(string accountId,
        IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        var account = Require(accountId, nameof(accountId));
        var scopedPaths = NormalizePaths(paths);
        if (scopedPaths.Length == 0) return [];
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            UPDATE MediaRecords SET IsDeleted = TRUE, DeletedUtc = COALESCE(DeletedUtc, NOW())
            WHERE AccountId = @accountId AND MediaPath = ANY(@paths) AND NOT IsDeleted
            RETURNING {Columns}
            """, connection, transaction);
        command.Parameters.AddWithValue("accountId", account);
        command.Parameters.AddWithValue("paths", scopedPaths);
        var result = await ReadManyAsync(command, cancellationToken);
        if (result.Count > 0) await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<IReadOnlyList<StoredMediaRecord>> ReadManyAsync(NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var result = new List<StoredMediaRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader));
        return result;
    }

    private static StoredMediaRecord Map(NpgsqlDataReader reader)
    {
        var deletedUtc = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset?>(12);
        return new StoredMediaRecord(new MediaRecord
        {
            Path = reader.GetString(0),
            CreatedUtc = reader.GetFieldValue<DateTimeOffset>(1),
            MediaType = reader.GetString(2),
            Reference = reader.GetString(3),
            AccountId = reader.GetString(4),
            LoopId = reader.GetString(5),
            Url = reader.GetString(6),
            IsEncrypted = reader.GetBoolean(9),
            IsDeleted = reader.GetBoolean(11),
            Meta = JsonSerializer.Deserialize<Dictionary<string, object?>>(reader.GetString(13)) ??
                   new Dictionary<string, object?>()
        }, reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.IsDBNull(10) ? null : reader.GetString(10), deletedUtc);
    }

    private static string[] NormalizePaths(IReadOnlyList<string> paths) => paths
        .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string Require(string value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("Value is required.", name);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
