using System.Collections.Concurrent;

namespace Jibo.Cloud.Application.Services;

public sealed class RepeatLastCommandStore(TimeSpan? ttl = null, TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, LastCommand> _lastCommands =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _ttl = ttl ?? DefaultTtl;

    public void Set(string? jiboKey, LastCommand command)
    {
        if (string.IsNullOrWhiteSpace(jiboKey)) return;
        _lastCommands[jiboKey.Trim()] = command with { ExpiresAtUtc = _timeProvider.GetUtcNow().Add(_ttl) };
    }

    public LastCommand? TryGet(string? jiboDeviceId, string? jiboFriendlyId)
    {
        PurgeExpired();

        var now = _timeProvider.GetUtcNow();
        foreach (var key in EnumerateKeys(jiboDeviceId, jiboFriendlyId))
            if (_lastCommands.TryGetValue(key, out var command) && command.ExpiresAtUtc > now)
                return command;

        return null;
    }

    /// <summary>
    ///     Reads the stored command and slides its expiry forward, so repeating a command counts as activity.
    /// </summary>
    public LastCommand? TryGetAndRenew(string? jiboDeviceId, string? jiboFriendlyId)
    {
        PurgeExpired();

        var now = _timeProvider.GetUtcNow();
        foreach (var key in EnumerateKeys(jiboDeviceId, jiboFriendlyId))
        {
            if (!_lastCommands.TryGetValue(key, out var command) || command.ExpiresAtUtc <= now) continue;

            _lastCommands[key] = command with { ExpiresAtUtc = now.Add(_ttl) };
            return command;
        }

        return null;
    }

    public void Clear(string? jiboDeviceId, string? jiboFriendlyId)
    {
        foreach (var key in EnumerateKeys(jiboDeviceId, jiboFriendlyId))
            _lastCommands.TryRemove(key, out _);
    }

    private void PurgeExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var pair in _lastCommands)
            if (pair.Value.ExpiresAtUtc <= now)
                _lastCommands.TryRemove(pair.Key, out _);
    }

    private static IEnumerable<string> EnumerateKeys(string? jiboDeviceId, string? jiboFriendlyId)
    {
        if (!string.IsNullOrWhiteSpace(jiboFriendlyId))
            yield return jiboFriendlyId.Trim();
        if (!string.IsNullOrWhiteSpace(jiboDeviceId) &&
            !string.Equals(jiboDeviceId, jiboFriendlyId, StringComparison.OrdinalIgnoreCase))
            yield return jiboDeviceId.Trim();
    }

    public sealed record LastCommand(
        string RawTranscript,
        string? NormalizedTranscript,
        IReadOnlyDictionary<string, object?> NluAttributes,
        DateTimeOffset ExpiresAtUtc = default);
}
