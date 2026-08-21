using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlBackupManifestRepository(PostgreSqlCloudStateDataSource dataSource)
    : IBackupManifestRepository
{
    private const string Columns = "BackupId, AccountId, LoopId, Name, BlobUri, ContentSha256, ContentLength, " +
                                   "BackupSchemaVersion, Status, CreatedUtc, ExpiresUtc, RestoredUtc";

    public async Task<IReadOnlyList<BackupManifestRecord>> ListAsync(string accountId, string? loopId = null,
        CancellationToken cancellationToken = default)
    {
        var account = Require(accountId, nameof(accountId));
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT {Columns} FROM BackupManifests
                               WHERE AccountId = @accountId AND (@loopId IS NULL OR LoopId = @loopId)
                               ORDER BY CreatedUtc DESC, BackupId
                               """;
        command.Parameters.AddWithValue("accountId", account);
        command.Parameters.Add("loopId", NpgsqlDbType.Text).Value = (object?)Normalize(loopId) ?? DBNull.Value;
        return await ReadManyAsync(command, cancellationToken);
    }

    public async Task<BackupManifestRecord?> GetAsync(string accountId, string backupId,
        CancellationToken cancellationToken = default)
    {
        var account = Require(accountId, nameof(accountId));
        var id = Require(backupId, nameof(backupId));
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM BackupManifests WHERE AccountId = @accountId AND BackupId = @id";
        command.Parameters.AddWithValue("accountId", account); command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<BackupManifestRecord> UpsertAsync(BackupManifestRecord backup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        var account = Require(backup.AccountId ?? string.Empty, nameof(backup.AccountId));
        Require(backup.BackupId, nameof(backup.BackupId)); Require(backup.BlobUri, nameof(backup.BlobUri));
        Require(backup.ContentSha256, nameof(backup.ContentSha256));
        if (backup.ContentLength < 0) throw new ArgumentOutOfRangeException(nameof(backup.ContentLength));
        if (backup.BackupSchemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(backup.BackupSchemaVersion));

        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO BackupManifests
                (BackupId, AccountId, LoopId, Name, BlobUri, ContentSha256, ContentLength,
                 BackupSchemaVersion, Status, CreatedUtc, ExpiresUtc, RestoredUtc)
            VALUES (@id, @account, @loop, @name, @uri, @sha, @length, @schema, @status, @created, @expires, @restored)
            ON CONFLICT (BackupId) DO UPDATE SET
                AccountId = EXCLUDED.AccountId, LoopId = EXCLUDED.LoopId, Name = EXCLUDED.Name,
                BlobUri = EXCLUDED.BlobUri, ContentSha256 = EXCLUDED.ContentSha256,
                ContentLength = EXCLUDED.ContentLength, BackupSchemaVersion = EXCLUDED.BackupSchemaVersion,
                Status = EXCLUDED.Status, CreatedUtc = EXCLUDED.CreatedUtc,
                ExpiresUtc = EXCLUDED.ExpiresUtc, RestoredUtc = EXCLUDED.RestoredUtc
            WHERE BackupManifests.AccountId = EXCLUDED.AccountId
            """, connection, transaction);
        command.Parameters.AddWithValue("id", backup.BackupId.Trim()); command.Parameters.AddWithValue("account", account);
        command.Parameters.Add("loop", NpgsqlDbType.Text).Value =
            (object?)Normalize(backup.LoopId) ?? DBNull.Value;
        command.Parameters.AddWithValue("name", string.IsNullOrWhiteSpace(backup.Name) ? "backup" : backup.Name.Trim());
        command.Parameters.AddWithValue("uri", backup.BlobUri.Trim());
        command.Parameters.AddWithValue("sha", backup.ContentSha256.Trim());
        command.Parameters.AddWithValue("length", backup.ContentLength);
        command.Parameters.AddWithValue("schema", backup.BackupSchemaVersion);
        command.Parameters.AddWithValue("status", string.IsNullOrWhiteSpace(backup.Status) ? "available" : backup.Status.Trim());
        command.Parameters.AddWithValue("created", backup.CreatedUtc);
        command.Parameters.Add("expires", NpgsqlDbType.TimestampTz).Value =
            (object?)backup.ExpiresUtc ?? DBNull.Value;
        command.Parameters.Add("restored", NpgsqlDbType.TimestampTz).Value =
            (object?)backup.RestoredUtc ?? DBNull.Value;
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
            throw new InvalidOperationException("The backup id belongs to another account.");
        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return backup;
    }

    public async Task<BackupManifestRecord?> MarkRestoredAsync(string accountId, string backupId,
        DateTimeOffset restoredUtc, CancellationToken cancellationToken = default)
    {
        var account = Require(accountId, nameof(accountId)); var id = Require(backupId, nameof(backupId));
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            UPDATE BackupManifests SET RestoredUtc = @restored, Status = 'restored'
            WHERE AccountId = @accountId AND BackupId = @id
            RETURNING {Columns}
            """, connection, transaction);
        command.Parameters.AddWithValue("restored", restoredUtc); command.Parameters.AddWithValue("accountId", account);
        command.Parameters.AddWithValue("id", id);
        BackupManifestRecord? result;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            result = await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
        if (result is not null) await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<IReadOnlyList<BackupManifestRecord>> ReadManyAsync(NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var result = new List<BackupManifestRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader));
        return result;
    }

    private static BackupManifestRecord Map(NpgsqlDataReader reader) => new(reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetInt32(7),
        reader.GetString(8), reader.GetFieldValue<DateTimeOffset>(9),
        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset?>(10),
        reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset?>(11));
    private static string Require(string value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("Value is required.", name);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
