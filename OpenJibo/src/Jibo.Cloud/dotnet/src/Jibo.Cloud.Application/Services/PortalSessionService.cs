using System.Collections.Concurrent;

namespace Jibo.Cloud.Application.Services;

public sealed class PortalSessionService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, PortalSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    public PortalSession CreateSession(string deviceId, string friendlyId)
    {
        PurgeExpired();

        var token = Guid.NewGuid().ToString("N");
        var session = new PortalSession(
            token,
            deviceId,
            friendlyId,
            DateTimeOffset.UtcNow.Add(SessionLifetime));

        _sessions[token] = session;
        return session;
    }

    public PortalSession? TryGetSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        PurgeExpired();

        return _sessions.TryGetValue(token.Trim(), out var session) &&
               session.ExpiresAtUtc > DateTimeOffset.UtcNow
            ? session
            : null;
    }

    public void RevokeSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        _sessions.TryRemove(token.Trim(), out _);
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _sessions)
        {
            if (pair.Value.ExpiresAtUtc > now) continue;
            _sessions.TryRemove(pair.Key, out _);
        }
    }

    public sealed record PortalSession(
        string Token,
        string DeviceId,
        string FriendlyId,
        DateTimeOffset ExpiresAtUtc);
}
