using System.Collections.Concurrent;

namespace Jibo.Cloud.Application.Services;

public sealed class HomeAssistantPendingClimateStore
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, PendingClimateAction> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    public void Set(string jiboKey, PendingClimateAction action)
    {
        if (string.IsNullOrWhiteSpace(jiboKey)) return;
        _pending[jiboKey.Trim()] = action with { ExpiresAtUtc = DateTimeOffset.UtcNow.Add(DefaultTtl) };
    }

    public PendingClimateAction? TryGet(string? jiboDeviceId, string? jiboFriendlyId)
    {
        PurgeExpired();

        foreach (var key in EnumerateKeys(jiboDeviceId, jiboFriendlyId))
            if (_pending.TryGetValue(key, out var pending) && pending.ExpiresAtUtc > DateTimeOffset.UtcNow)
                return pending;

        return null;
    }

    public void Clear(string? jiboDeviceId, string? jiboFriendlyId)
    {
        foreach (var key in EnumerateKeys(jiboDeviceId, jiboFriendlyId))
            _pending.TryRemove(key, out _);
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _pending)
            if (pair.Value.ExpiresAtUtc <= now)
                _pending.TryRemove(pair.Key, out _);
    }

    private static IEnumerable<string> EnumerateKeys(string? jiboDeviceId, string? jiboFriendlyId)
    {
        if (!string.IsNullOrWhiteSpace(jiboFriendlyId))
            yield return jiboFriendlyId.Trim();
        if (!string.IsNullOrWhiteSpace(jiboDeviceId) &&
            !string.Equals(jiboDeviceId, jiboFriendlyId, StringComparison.OrdinalIgnoreCase))
            yield return jiboDeviceId.Trim();
    }

    public sealed record PendingClimateAction(
        string Action,
        IReadOnlyList<HomeAssistantCommandCandidate> Candidates,
        string? Temperature = null,
        string? Delta = null,
        DateTimeOffset ExpiresAtUtc = default);
}
