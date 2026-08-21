using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal static class CloudAuthTokenHasher
{
    internal static string Hash(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("A token is required.", nameof(token));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}

public sealed class UserDataCloudStateSecretProtector(
    UserDataEncryptionService encryptionService,
    string keyId = "openjibo-user-data-v1") : ICloudStateSecretProtector
{
    public string KeyId { get; } = keyId;

    public byte[] Protect(string plaintext) => Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(encryptionService.Encrypt(plaintext)));

    public string Unprotect(byte[] ciphertext)
    {
        var payload = JsonSerializer.Deserialize<UserDataEncryptionService.EncryptedPayload>(ciphertext)
                      ?? throw new InvalidOperationException("The encrypted cloud-state secret is invalid.");
        return encryptionService.Decrypt(payload);
    }
}

internal static class CloudStateRevision
{
    internal static async Task<long> BumpAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
                                                     UPDATE CloudStateMetadata
                                                     SET Revision = Revision + 1, UpdatedUtc = NOW()
                                                     WHERE StateKey = 'cloud-state'
                                                     RETURNING Revision
                                                     """, connection, transaction);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ??
                      throw new InvalidOperationException(
                          "CloudStateMetadata is missing. Run the state migrations before starting the service."));
    }
}

internal static class NpgsqlParameterHelpers
{
    internal static void AddNullable(NpgsqlParameterCollection parameters, string name, NpgsqlDbType type,
        object? value)
    {
        parameters.Add(name, type).Value = value ?? DBNull.Value;
    }
}

internal sealed class BoundedExpiringCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries;
    private readonly int _maxEntries;
    private readonly TimeSpan _ttl;
    private readonly Lock _syncRoot = new();
    private readonly TimeProvider _timeProvider;

    internal BoundedExpiringCache(int maxEntries, TimeSpan ttl, IEqualityComparer<TKey>? comparer = null,
        TimeProvider? timeProvider = null)
    {
        _maxEntries = Math.Max(1, maxEntries);
        _ttl = ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(5);
        _entries = new Dictionary<TKey, Entry>(comparer);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal int Count
    {
        get
        {
            lock (_syncRoot) return _entries.Count;
        }
    }

    internal bool TryGet(TKey key, out TValue value)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_syncRoot)
        {
            if (_entries.TryGetValue(key, out var entry) && entry.ExpiresUtc > now)
            {
                entry.LastAccessUtc = now;
                value = entry.Value;
                return true;
            }

            _entries.Remove(key);
        }

        value = default!;
        return false;
    }

    internal void Set(TKey key, TValue value)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_syncRoot)
        {
            if (!_entries.ContainsKey(key) && _entries.Count >= _maxEntries)
            {
                var oldest = _entries.MinBy(pair => pair.Value.LastAccessUtc).Key;
                _entries.Remove(oldest);
            }

            _entries[key] = new Entry(value, now + _ttl, now);
        }
    }

    internal void Remove(TKey key)
    {
        lock (_syncRoot) _entries.Remove(key);
    }

    internal void Clear()
    {
        lock (_syncRoot) _entries.Clear();
    }

    private sealed class Entry(TValue value, DateTimeOffset expiresUtc, DateTimeOffset lastAccessUtc)
    {
        internal TValue Value { get; } = value;
        internal DateTimeOffset ExpiresUtc { get; } = expiresUtc;
        internal DateTimeOffset LastAccessUtc { get; set; } = lastAccessUtc;
    }
}
