using System.Collections.Concurrent;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// In-memory pending notification queue for robot api-socket delivery.
/// Coalesces overlapping LoopUpdated notifications and expires stale entries.
/// </summary>
public sealed class RobotPendingNotificationStore
{
    // Keep LoopUpdated until the robot reconnects; 5m was too short for offline portals.
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<Guid, PendingNotificationEntry> _entries = new();
    private readonly TimeSpan _ttl;

    public RobotPendingNotificationStore(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? DefaultTtl;
    }

    public int Count
    {
        get
        {
            PruneExpired();
            return _entries.Count;
        }
    }

    public void Enqueue(string notificationName, IReadOnlyCollection<string> robotKeys, byte[] payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationName);
        ArgumentNullException.ThrowIfNull(payload);

        var keys = NormalizeKeys(robotKeys);
        if (keys.Count == 0 || payload.Length == 0) return;

        PruneExpired();

        // Coalesce by overlap for the same notification type (LoopUpdated).
        foreach (var pair in _entries)
        {
            var existing = pair.Value;
            if (!existing.Name.Equals(notificationName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!existing.RobotKeys.Overlaps(keys)) continue;
            _entries.TryRemove(pair.Key, out _);
        }

        _entries[Guid.NewGuid()] = new PendingNotificationEntry(
            notificationName,
            keys,
            payload,
            DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<byte[]> Drain(IReadOnlyCollection<string> robotKeys)
    {
        var keys = NormalizeKeys(robotKeys);
        if (keys.Count == 0) return [];

        PruneExpired();

        var drained = new List<byte[]>();
        foreach (var pair in _entries)
        {
            var entry = pair.Value;
            if (!entry.RobotKeys.Overlaps(keys)) continue;
            if (!_entries.TryRemove(pair.Key, out var removed)) continue;
            drained.Add(removed.Payload);
        }

        return drained;
    }

    private void PruneExpired()
    {
        if (_entries.IsEmpty) return;

        var cutoff = DateTimeOffset.UtcNow - _ttl;
        foreach (var pair in _entries)
        {
            if (pair.Value.CreatedUtc >= cutoff) continue;
            _entries.TryRemove(pair.Key, out _);
        }
    }

    private static HashSet<string> NormalizeKeys(IReadOnlyCollection<string>? robotKeys)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (robotKeys is null) return keys;

        foreach (var key in robotKeys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            keys.Add(key.Trim());
        }

        return keys;
    }

    private sealed record PendingNotificationEntry(
        string Name,
        HashSet<string> RobotKeys,
        byte[] Payload,
        DateTimeOffset CreatedUtc);
}
