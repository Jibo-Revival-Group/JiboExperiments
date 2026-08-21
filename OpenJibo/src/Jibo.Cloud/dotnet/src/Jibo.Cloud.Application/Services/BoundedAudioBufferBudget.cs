namespace Jibo.Cloud.Application.Services;

internal sealed class BoundedAudioBufferBudget
{
    private readonly Dictionary<string, int> _reservations = new(StringComparer.Ordinal);
    private readonly long _maximumBytes;
    private readonly Lock _syncRoot = new();
    private long _reservedBytes;

    internal BoundedAudioBufferBudget(long maximumBytes)
    {
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _maximumBytes = maximumBytes;
    }

    internal long ReservedBytes
    {
        get
        {
            lock (_syncRoot) return _reservedBytes;
        }
    }

    internal bool TryReserve(string sessionId, int bytes)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("A session id is required.", nameof(sessionId));
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        lock (_syncRoot)
        {
            if (_reservedBytes > _maximumBytes - bytes) return false;
            _reservations.TryGetValue(sessionId, out var current);
            _reservations[sessionId] = checked(current + bytes);
            _reservedBytes += bytes;
            return true;
        }
    }

    internal void Release(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        lock (_syncRoot)
        {
            if (!_reservations.Remove(sessionId, out var bytes)) return;
            _reservedBytes -= bytes;
        }
    }
}
