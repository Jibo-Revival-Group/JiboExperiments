using System.Collections.Concurrent;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

/// <summary>
/// Separates compact, restart-safe authentication records from bounded live dialog sessions.
/// Active sessions are never part of the durable cloud-state snapshot.
/// </summary>
internal sealed class BoundedCloudSessionRegistry(int maximumActiveSessions = 256, int maximumDurableTokens = 256,
    ITransportMetrics? transportMetrics = null)
{
    private readonly ConcurrentDictionary<string, CloudSession> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CloudSession> _durableTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maximumActiveSessions = Math.Max(1, maximumActiveSessions);
    private readonly int _maximumDurableTokens = Math.Max(1, maximumDurableTokens);
    private readonly ITransportMetrics _transportMetrics = transportMetrics ?? NullTransportMetrics.Instance;

    public IReadOnlyCollection<CloudSession> Values
    {
        get
        {
            RemoveExpiredDurableTokens();
            var active = _active.ToArray();
            var activeKeys = active.Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return active.Select(pair => pair.Value)
                .Concat(_durableTokens.Where(pair => !activeKeys.Contains(pair.Key)).Select(pair => pair.Value))
                .ToArray();
        }
    }

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            RemoveExpiredDurableTokens();
            return _active.Keys.Concat(_durableTokens.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public IReadOnlyCollection<CloudSession> DurableTokenValues
    {
        get
        {
            RemoveExpiredDurableTokens();
            return _durableTokens.Values.ToArray();
        }
    }

    public void RegisterDurableToken(string token, CloudSession session)
    {
        _durableTokens[token] = session;
        var overflow = _durableTokens.Count - _maximumDurableTokens;
        if (overflow <= 0) return;
        foreach (var staleToken in _durableTokens
                     .OrderBy(pair => pair.Value.CreatedUtc)
                     .Take(overflow)
                     .Select(pair => pair.Key)
                     .ToArray())
            _durableTokens.TryRemove(staleToken, out _);
    }

    public void RegisterActive(string token, CloudSession session)
    {
        if (_active.TryAdd(token, session))
            _transportMetrics.ActiveSessionsChanged(1);
        else
            _active[token] = session;
        var overflow = _active.Count - _maximumActiveSessions;
        if (overflow <= 0) return;

        foreach (var staleToken in _active
                     .OrderBy(pair => pair.Value.LastSeenUtc)
                     .ThenBy(pair => pair.Value.CreatedUtc)
                     .Take(overflow)
                     .Select(pair => pair.Key)
                     .ToArray())
            if (_active.TryRemove(staleToken, out _)) _transportMetrics.ActiveSessionsChanged(-1);
    }

    public CloudSession? Find(string token) =>
        _active.GetValueOrDefault(token) ?? FindDurable(token);

    public CloudSession? FindActive(string token) => _active.GetValueOrDefault(token);
    public CloudSession? FindDurable(string token)
    {
        var durable = _durableTokens.GetValueOrDefault(token);
        if (durable?.ExpiresUtc is null || durable.ExpiresUtc > DateTimeOffset.UtcNow) return durable;
        _durableTokens.TryRemove(token, out _);
        return null;
    }

    public bool RemoveBySessionId(string sessionId)
    {
        var match = _active.FirstOrDefault(pair =>
            pair.Value.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase));
        if (match.Key is null || !_active.TryRemove(match.Key, out _)) return false;
        _transportMetrics.ActiveSessionsChanged(-1);
        return true;
    }

    public bool TryRemove(string token, out CloudSession? session)
    {
        var removedActive = _active.TryRemove(token, out var active);
        var removedDurable = _durableTokens.TryRemove(token, out var durable);
        session = active ?? durable;
        if (removedActive) _transportMetrics.ActiveSessionsChanged(-1);
        return removedActive || removedDurable;
    }

    public int RemoveDurableForDevice(string deviceId, string kind)
    {
        var removed = 0;
        foreach (var pair in _durableTokens.Where(pair =>
                     string.Equals(pair.Value.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(pair.Value.Kind, kind, StringComparison.OrdinalIgnoreCase)).ToArray())
            if (_durableTokens.TryRemove(pair.Key, out _)) removed++;
        return removed;
    }

    private void RemoveExpiredDurableTokens()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _durableTokens.Where(pair => pair.Value.ExpiresUtc <= now).ToArray())
            _durableTokens.TryRemove(pair.Key, out _);
    }

    public void Clear()
    {
        var activeCount = _active.Count;
        _active.Clear();
        _durableTokens.Clear();
        if (activeCount > 0) _transportMetrics.ActiveSessionsChanged(-activeCount);
    }
}
