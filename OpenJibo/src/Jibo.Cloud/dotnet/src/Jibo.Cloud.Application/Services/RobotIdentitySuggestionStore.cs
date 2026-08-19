using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

public sealed class RobotIdentitySuggestionStore(ICloudStateStore cloudStateStore)
{
    private const int MaxCandidatesPerDevice = 4;
    private const int MaxEvidencePerCandidate = 8;
    private const int MaxTrackedDevices = 1000;
    private static readonly TimeSpan SuggestionTtl = TimeSpan.FromDays(30);
    private readonly Dictionary<string, Dictionary<string, CandidateState>> _candidates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _syncRoot = new();

    public void Observe(string? deviceId, string? candidate, string source, string field)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || !IsSafeIdentityName(candidate)) return;

        var device = ResolveDevice(deviceId);
        if (device is null || MatchesCurrentIdentity(device, candidate!)) return;

        var normalizedCandidate = candidate!.Trim();
        var now = DateTimeOffset.UtcNow;
        lock (_syncRoot)
        {
            PurgeExpiredLocked(now);
            if (!_candidates.TryGetValue(device.DeviceId, out var deviceCandidates))
            {
                if (_candidates.Count >= MaxTrackedDevices)
                {
                    var oldestDeviceId = _candidates
                        .OrderBy(pair => pair.Value.Values.Max(candidate => candidate.LastObservedUtc))
                        .First().Key;
                    _candidates.Remove(oldestDeviceId);
                }
                deviceCandidates = new Dictionary<string, CandidateState>(StringComparer.OrdinalIgnoreCase);
                _candidates[device.DeviceId] = deviceCandidates;
            }

            if (!deviceCandidates.TryGetValue(normalizedCandidate, out var state))
            {
                if (deviceCandidates.Count >= MaxCandidatesPerDevice)
                {
                    var weakest = deviceCandidates
                        .OrderBy(pair => pair.Value.ObservationCount)
                        .ThenBy(pair => pair.Value.LastObservedUtc)
                        .First();
                    deviceCandidates.Remove(weakest.Key);
                }

                state = new CandidateState(normalizedCandidate, now);
                deviceCandidates[normalizedCandidate] = state;
            }

            if (state.ObservationCount < int.MaxValue) state.ObservationCount++;
            state.LastObservedUtc = now;
            var evidence = new RobotIdentitySuggestionEvidence(source, field, normalizedCandidate, now);
            if (!state.Evidence.Any(item =>
                    item.Source.Equals(evidence.Source, StringComparison.OrdinalIgnoreCase) &&
                    item.Field.Equals(evidence.Field, StringComparison.OrdinalIgnoreCase) &&
                    item.Value.Equals(evidence.Value, StringComparison.OrdinalIgnoreCase)))
            {
                state.Evidence.Add(evidence);
                if (state.Evidence.Count > MaxEvidencePerCandidate)
                    state.Evidence.RemoveAt(0);
            }
        }
    }

    public RobotIdentitySuggestion? GetSuggestion(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        var device = ResolveDevice(deviceId);
        if (device is null) return null;

        lock (_syncRoot)
        {
            PurgeExpiredLocked(DateTimeOffset.UtcNow);
            if (!_candidates.TryGetValue(device.DeviceId, out var deviceCandidates)) return null;

            foreach (var stale in deviceCandidates
                         .Where(pair => MatchesCurrentIdentity(device, pair.Key))
                         .Select(pair => pair.Key)
                         .ToArray())
                deviceCandidates.Remove(stale);

            var best = deviceCandidates.Values
                .OrderByDescending(candidate => candidate.ObservationCount)
                .ThenByDescending(candidate => candidate.LastObservedUtc)
                .FirstOrDefault();
            if (best is null) return null;

            var target = cloudStateStore.FindDeviceByFriendlyId(best.ProposedRobotId);
            return new RobotIdentitySuggestion(
                device.DeviceId,
                device.RobotId,
                best.ProposedRobotId,
                target is null || target.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase)
                    ? "rename"
                    : "merge",
                target?.DeviceId,
                best.ObservationCount,
                best.FirstObservedUtc,
                best.LastObservedUtc,
                best.Evidence.ToArray());
        }
    }

    public void Dismiss(string? deviceId, string? proposedRobotId = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        lock (_syncRoot)
        {
            if (string.IsNullOrWhiteSpace(proposedRobotId))
            {
                _candidates.Remove(deviceId.Trim());
                return;
            }

            if (!_candidates.TryGetValue(deviceId.Trim(), out var candidates)) return;
            candidates.Remove(proposedRobotId.Trim());
            if (candidates.Count == 0) _candidates.Remove(deviceId.Trim());
        }
    }

    public static bool IsSafeIdentityName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Length <= 120 &&
        System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), "^[A-Za-z0-9]+(?:-[A-Za-z0-9]+){2,}$") &&
        !value.Trim().StartsWith("robot-", StringComparison.OrdinalIgnoreCase);

    private DeviceRegistration? ResolveDevice(string deviceId)
    {
        return cloudStateStore.FindDeviceByFriendlyId(deviceId.Trim());
    }

    private static bool MatchesCurrentIdentity(DeviceRegistration device, string candidate)
    {
        var normalized = candidate.Trim();
        return new[] { device.DeviceId, device.RobotId, device.FriendlyName, device.VerifiedSerialNumber }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(value => value!.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private void PurgeExpiredLocked(DateTimeOffset now)
    {
        foreach (var deviceId in _candidates.Keys.ToArray())
        {
            var candidates = _candidates[deviceId];
            foreach (var candidate in candidates
                         .Where(pair => now - pair.Value.LastObservedUtc > SuggestionTtl)
                         .Select(pair => pair.Key)
                         .ToArray())
                candidates.Remove(candidate);
            if (candidates.Count == 0) _candidates.Remove(deviceId);
        }
    }

    private sealed class CandidateState(string proposedRobotId, DateTimeOffset observedUtc)
    {
        public string ProposedRobotId { get; } = proposedRobotId;
        public int ObservationCount { get; set; }
        public DateTimeOffset FirstObservedUtc { get; } = observedUtc;
        public DateTimeOffset LastObservedUtc { get; set; } = observedUtc;
        public List<RobotIdentitySuggestionEvidence> Evidence { get; } = [];
    }
}

public sealed record RobotIdentitySuggestion(
    string DeviceId,
    string CurrentRobotId,
    string ProposedRobotId,
    string Action,
    string? TargetDeviceId,
    int ObservationCount,
    DateTimeOffset FirstObservedUtc,
    DateTimeOffset LastObservedUtc,
    IReadOnlyList<RobotIdentitySuggestionEvidence> Evidence);

public sealed record RobotIdentitySuggestionEvidence(
    string Source,
    string Field,
    string Value,
    DateTimeOffset ObservedUtc);

public sealed record RobotIdentityCandidate(string Field, string Value);

public static class RobotIdentityCandidateExtractor
{
    private static readonly HashSet<string> CandidateFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "robotFriendlyId", "friendlyId", "robotID", "robotId", "robot_name", "robotName"
    };

    public static IReadOnlyList<RobotIdentityCandidate> Extract(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 256 * 1024)
            return [];

        var syslogCandidates = ExtractSyslogCandidates(json);
        if (!CandidateFields.Any(field => json.Contains(field, StringComparison.OrdinalIgnoreCase)) &&
            !json.Contains("\"jibo\"", StringComparison.OrdinalIgnoreCase) &&
            !json.Contains("\"serial_number\"", StringComparison.OrdinalIgnoreCase))
            return syslogCandidates;

        try
        {
            using var document = JsonDocument.Parse(json);
            var candidates = new List<RobotIdentityCandidate>(syslogCandidates);
            Visit(document.RootElement, string.Empty, candidates);
            return candidates
                .Where(candidate => RobotIdentitySuggestionStore.IsSafeIdentityName(candidate.Value))
                .DistinctBy(candidate => $"{candidate.Field}\0{candidate.Value}", StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return syslogCandidates;
        }
    }

    private static void Visit(JsonElement element, string path, List<RobotIdentityCandidate> candidates)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var isRobotHealthRoot = string.IsNullOrEmpty(path) &&
                                    element.TryGetProperty("serial_number", out var serial) &&
                                    serial.ValueKind == JsonValueKind.String;
            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
                var isJiboId = property.NameEquals("id") &&
                               path.EndsWith(".jibo", StringComparison.OrdinalIgnoreCase);
                var isRobotHealthName = isRobotHealthRoot && property.NameEquals("name");
                if (property.Value.ValueKind == JsonValueKind.String &&
                    (CandidateFields.Contains(property.Name) || isJiboId || isRobotHealthName) &&
                    property.Value.GetString() is { } value)
                    candidates.Add(new RobotIdentityCandidate(propertyPath, value.Trim()));
                Visit(property.Value, propertyPath, candidates);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                Visit(item, $"{path}[{index++}]", candidates);
        }
    }

    private static IReadOnlyList<RobotIdentityCandidate> ExtractSyslogCandidates(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(
            text,
            "(?m)^\\d{4}-\\d{2}-\\d{2}T\\S+\\s+([A-Za-z0-9]+(?:-[A-Za-z0-9]+){2,})\\s+",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return matches
            .Select(match => new RobotIdentityCandidate("syslog.hostname", match.Groups[1].Value))
            .Where(candidate => RobotIdentitySuggestionStore.IsSafeIdentityName(candidate.Value))
            .DistinctBy(candidate => candidate.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
