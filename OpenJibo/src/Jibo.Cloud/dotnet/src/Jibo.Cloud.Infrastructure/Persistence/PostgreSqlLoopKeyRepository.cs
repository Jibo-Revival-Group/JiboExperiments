using Jibo.Cloud.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlLoopKeyRepository(PostgreSqlCloudStateDataSource dataSource) : ILoopKeyRepository
{
    public async Task<LoopSymmetricKeyRecord?> GetAsync(string accountId, string loopId, CancellationToken ct = default)
    { await using var c = await dataSource.Value.OpenConnectionAsync(ct); await using var q = c.CreateCommand(); q.CommandText = """
      SELECT k.LoopId,k.EncryptedKey,k.WrappingKeyId,k.Algorithm,k.CreatedUtc,k.RotatedUtc FROM LoopSymmetricKeys k
      INNER JOIN Loops l ON l.LoopId=k.LoopId WHERE l.OwnerAccountId=@a AND k.LoopId=@l
      """; Scope(q, accountId, loopId); await using var r = await q.ExecuteReaderAsync(ct); return await r.ReadAsync(ct) ? MapKey(r) : null; }
    public async Task<LoopSymmetricKeyRecord> UpsertAsync(string accountId, LoopSymmetricKeyRecord key, CancellationToken ct = default)
    { ArgumentNullException.ThrowIfNull(key); await using var c = await dataSource.Value.OpenConnectionAsync(ct); await using var t = await c.BeginTransactionAsync(ct); await using var q = new NpgsqlCommand("""
      INSERT INTO LoopSymmetricKeys(LoopId,EncryptedKey,WrappingKeyId,Algorithm,CreatedUtc,RotatedUtc)
      SELECT @l,@encrypted,@wrapping,@algorithm,@created,@rotated WHERE EXISTS(SELECT 1 FROM Loops WHERE LoopId=@l AND OwnerAccountId=@a)
      ON CONFLICT(LoopId) DO UPDATE SET EncryptedKey=EXCLUDED.EncryptedKey,WrappingKeyId=EXCLUDED.WrappingKeyId,Algorithm=EXCLUDED.Algorithm,RotatedUtc=EXCLUDED.RotatedUtc
      """, c, t); Scope(q, accountId, key.LoopId); q.Parameters.AddWithValue("encrypted", key.EncryptedKey); q.Parameters.AddWithValue("wrapping", key.WrappingKeyId); q.Parameters.AddWithValue("algorithm", key.Algorithm); q.Parameters.AddWithValue("created", key.CreatedUtc); q.Parameters.Add("rotated", NpgsqlDbType.TimestampTz).Value = (object?)key.RotatedUtc ?? DBNull.Value; if (await q.ExecuteNonQueryAsync(ct) == 0) throw new InvalidOperationException("Loop scope was not found."); await CloudStateRevision.BumpAsync(c, t, ct); await t.CommitAsync(ct); return key; }
    public async Task<IReadOnlyList<StoredKeyRequest>> ListRequestsAsync(string accountId, string loopId, int limit = 100, CancellationToken ct = default)
    { await using var c = await dataSource.Value.OpenConnectionAsync(ct); await using var q = c.CreateCommand(); q.CommandText = """
      SELECT k.RequestId,k.LoopId,k.PublicKey,k.EncryptedKey,k.CreatedUtc,k.RequestKind,k.Status,k.CompletedUtc FROM KeyRequests k
      INNER JOIN Loops l ON l.LoopId=k.LoopId WHERE l.OwnerAccountId=@a AND k.LoopId=@l ORDER BY k.CreatedUtc DESC LIMIT @limit
      """; Scope(q, accountId, loopId); q.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500)); var x = new List<StoredKeyRequest>(); await using var r = await q.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) x.Add(MapRequest(r)); return x; }
    public async Task<IReadOnlyList<StoredKeyRequest>> ListAllRequestsForBackupAsync(string accountId,
        string loopId, CancellationToken ct = default)
    { await using var c = await dataSource.Value.OpenConnectionAsync(ct); await using var q = c.CreateCommand(); q.CommandText = """
      SELECT k.RequestId,k.LoopId,k.PublicKey,k.EncryptedKey,k.CreatedUtc,k.RequestKind,k.Status,k.CompletedUtc FROM KeyRequests k
      INNER JOIN Loops l ON l.LoopId=k.LoopId WHERE l.OwnerAccountId=@a AND k.LoopId=@l ORDER BY k.CreatedUtc,k.RequestId
      """; Scope(q, accountId, loopId); var x = new List<StoredKeyRequest>(); await using var r = await q.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) x.Add(MapRequest(r)); return x; }
    public async Task<StoredKeyRequest> UpsertRequestAsync(string accountId, StoredKeyRequest request, CancellationToken ct = default)
    { ArgumentNullException.ThrowIfNull(request); var x = request.Request; await using var c = await dataSource.Value.OpenConnectionAsync(ct); await using var t = await c.BeginTransactionAsync(ct); await using var q = new NpgsqlCommand("""
      INSERT INTO KeyRequests(RequestId,LoopId,PublicKey,EncryptedKey,RequestKind,Status,CreatedUtc,CompletedUtc)
      SELECT @id,@l,@public,@encrypted,@kind,@status,@created,@completed WHERE EXISTS(SELECT 1 FROM Loops WHERE LoopId=@l AND OwnerAccountId=@a)
      ON CONFLICT(RequestId) DO UPDATE SET PublicKey=EXCLUDED.PublicKey,EncryptedKey=EXCLUDED.EncryptedKey,RequestKind=EXCLUDED.RequestKind,Status=EXCLUDED.Status,CompletedUtc=EXCLUDED.CompletedUtc WHERE KeyRequests.LoopId=EXCLUDED.LoopId
      """, c, t); Scope(q, accountId, x.LoopId); q.Parameters.AddWithValue("id", Req(x.RequestId)); q.Parameters.AddWithValue("public", x.PublicKey); q.Parameters.AddWithValue("encrypted", x.EncryptedKey); q.Parameters.AddWithValue("kind", request.RequestKind); q.Parameters.AddWithValue("status", request.Status); q.Parameters.AddWithValue("created", x.CreatedUtc); q.Parameters.Add("completed", NpgsqlDbType.TimestampTz).Value = (object?)request.CompletedUtc ?? DBNull.Value; if (await q.ExecuteNonQueryAsync(ct) == 0) throw new InvalidOperationException("Loop scope was not found."); await CloudStateRevision.BumpAsync(c, t, ct); await t.CommitAsync(ct); return request; }
    public async Task<bool> DeleteRequestAsync(string accountId, string loopId, string requestId, CancellationToken ct = default)
    { await using var c = await dataSource.Value.OpenConnectionAsync(ct); await using var t = await c.BeginTransactionAsync(ct); await using var q = new NpgsqlCommand("DELETE FROM KeyRequests k USING Loops l WHERE l.LoopId=k.LoopId AND l.OwnerAccountId=@a AND k.LoopId=@l AND k.RequestId=@id", c, t); Scope(q, accountId, loopId); q.Parameters.AddWithValue("id", Req(requestId)); var ok = await q.ExecuteNonQueryAsync(ct) > 0; if (ok) await CloudStateRevision.BumpAsync(c, t, ct); await t.CommitAsync(ct); return ok; }
    private static LoopSymmetricKeyRecord MapKey(NpgsqlDataReader r) => new(r.GetString(0), r.GetFieldValue<byte[]>(1), r.GetString(2), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4), r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5));
    private static StoredKeyRequest MapRequest(NpgsqlDataReader r) => new(new KeyRequestRecord { RequestId = r.GetString(0), LoopId = r.GetString(1), PublicKey = r.GetString(2), EncryptedKey = r.GetString(3), CreatedUtc = r.GetFieldValue<DateTimeOffset>(4) }, r.GetString(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetFieldValue<DateTimeOffset>(7));
    private static void Scope(NpgsqlCommand q, string a, string l) { q.Parameters.AddWithValue("a", Req(a)); q.Parameters.AddWithValue("l", Req(l)); }
    private static string Req(string v) => !string.IsNullOrWhiteSpace(v) ? v.Trim() : throw new ArgumentException("Value is required.");
}
