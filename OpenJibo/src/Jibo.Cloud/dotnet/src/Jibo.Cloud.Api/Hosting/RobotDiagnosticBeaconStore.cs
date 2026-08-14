using System.Collections.Concurrent;

namespace Jibo.Cloud.Api.Hosting;

/// <summary>
/// Bounded in-memory diagnostic buffer for robot-initiated LRD beacons.
/// Ingestion must be performed only after the caller has resolved an explicit
/// enrollment credential to a canonical robot identity.
/// </summary>
public sealed class RobotDiagnosticBeaconStore
{
    private const int MaxLinesPerRobot = 500;
    private static readonly TimeSpan BeaconExpiry = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, BeaconBuffer> buffers = new(StringComparer.OrdinalIgnoreCase);

    public void Publish(string robotId, IEnumerable<string> lines, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(robotId)) throw new ArgumentException("Robot ID is required.", nameof(robotId));

        var buffer = buffers.GetOrAdd(robotId.Trim(), _ => new BeaconBuffer());
        lock (buffer.SyncRoot)
        {
            foreach (var line in lines.Where(line => !string.IsNullOrEmpty(line)))
            {
                buffer.Lines.Enqueue(line);
                while (buffer.Lines.Count > MaxLinesPerRobot) buffer.Lines.Dequeue();
            }

            buffer.LastSeenUtc = now;
        }
    }

    public IReadOnlyList<string> Snapshot(string robotId, DateTimeOffset now)
    {
        if (!buffers.TryGetValue(robotId, out var buffer)) return Array.Empty<string>();
        lock (buffer.SyncRoot)
        {
            if (now - buffer.LastSeenUtc > BeaconExpiry) return Array.Empty<string>();
            return buffer.Lines.ToArray();
        }
    }

    public IReadOnlyList<RobotDiagnosticBeaconSummary> GetActive(DateTimeOffset now)
    {
        var active = new List<RobotDiagnosticBeaconSummary>();
        foreach (var pair in buffers)
        {
            lock (pair.Value.SyncRoot)
            {
                if (now - pair.Value.LastSeenUtc <= BeaconExpiry)
                    active.Add(new RobotDiagnosticBeaconSummary(pair.Key, pair.Value.LastSeenUtc, pair.Value.Lines.Count));
            }
        }

        return active.OrderByDescending(item => item.LastSeenUtc).ToArray();
    }

    private sealed class BeaconBuffer
    {
        public object SyncRoot { get; } = new();
        public Queue<string> Lines { get; } = new();
        public DateTimeOffset LastSeenUtc { get; set; }
    }
}

public sealed record RobotDiagnosticBeaconSummary(string RobotId, DateTimeOffset LastSeenUtc, int LineCount);

public sealed record RobotDiagnosticBeaconPublishRequest(IReadOnlyList<string> Lines);
