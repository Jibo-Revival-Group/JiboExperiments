using System.Collections.Concurrent;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Short-lived server reports form the transport-neutral basis for a federated fleet view.
/// Peer replication can publish the same contract later without changing the dashboard API.
/// </summary>
public sealed class FleetNetworkPresenceRegistry
{
    private readonly ConcurrentDictionary<string, FleetServerPresenceReport> _reports =
        new(StringComparer.OrdinalIgnoreCase);

    public void Report(FleetServerPresenceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(report.ServerId))
            throw new ArgumentException("ServerId is required.", nameof(report));

        _reports[report.ServerId.Trim()] = report with
        {
            ServerId = report.ServerId.Trim(),
            CanonicalHost = report.CanonicalHost.Trim(),
            ConnectedRobotIds = NormalizeRobotIds(report.ConnectedRobotIds),
            ConnectionCount = Math.Max(0, report.ConnectionCount)
        };
    }

    public IReadOnlyList<FleetServerPresenceReport> GetFreshReports(TimeSpan freshnessWindow, DateTimeOffset now)
    {
        var reports = new List<FleetServerPresenceReport>();
        foreach (var pair in _reports)
        {
            var report = pair.Value;
            if (now - report.ReportedAtUtc > freshnessWindow)
            {
                _reports.TryRemove(pair.Key, out _);
                continue;
            }

            reports.Add(report);
        }

        return reports.OrderBy(report => report.CanonicalHost, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> NormalizeRobotIds(IReadOnlyCollection<string>? robotIds) =>
        (robotIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public sealed record FleetServerPresenceReport(
    string ServerId,
    string CanonicalHost,
    string InstanceId,
    IReadOnlyCollection<string> ConnectedRobotIds,
    int ConnectionCount,
    DateTimeOffset ReportedAtUtc,
    bool IsLocal = false);

public sealed record FleetPeerPresencePayload(
    string ServerId,
    string CanonicalHost,
    string InstanceId,
    IReadOnlyCollection<string> ConnectedRobotIds,
    int ConnectionCount,
    DateTimeOffset ReportedAtUtc);

public sealed class OpenJiboServerIdentity
{
    public OpenJiboServerIdentity(string? canonicalHost)
    {
        CanonicalHost = string.IsNullOrWhiteSpace(canonicalHost) ? "local.openjibo" : canonicalHost.Trim();
        ServerId = CanonicalHost.ToLowerInvariant();
        InstanceId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
    }

    public string ServerId { get; }
    public string CanonicalHost { get; }
    public string InstanceId { get; }
}
