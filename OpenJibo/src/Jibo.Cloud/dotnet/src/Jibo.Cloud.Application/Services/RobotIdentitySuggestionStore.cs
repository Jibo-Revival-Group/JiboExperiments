using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

public sealed class RobotIdentitySuggestionStore
{
    private const int MaxCandidatesPerDevice = 4;
    private const int MaxEvidencePerCandidate = 8;
    private const int MaxTrackedDevices = 1000;
    private static readonly TimeSpan SuggestionTtl = TimeSpan.FromDays(30);
    private readonly ICloudStateStore cloudStateStore;
    private readonly IRobotIdentitySuggestionRepository? repository;
    private readonly Dictionary<string, Dictionary<string, CandidateState>> _candidates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _syncRoot = new();
    private readonly TimeProvider _timeProvider;

    public RobotIdentitySuggestionStore(
        ICloudStateStore cloudStateStore,
        IRobotIdentitySuggestionRepository? repository = null)
        : this(cloudStateStore, repository, TimeProvider.System)
    {
    }

    internal RobotIdentitySuggestionStore(
        ICloudStateStore cloudStateStore,
        IRobotIdentitySuggestionRepository? repository,
        TimeProvider timeProvider)
    {
        this.cloudStateStore = cloudStateStore;
        this.repository = repository;
        _timeProvider = timeProvider;
    }

    public void Observe(string? deviceId, string? candidate, string source, string field)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || !IsSafeIdentityName(candidate)) return;

        var device = ResolveDevice(deviceId);
        if (device is null || MatchesCurrentIdentity(device, candidate!)) return;

        var normalizedCandidate = candidate!.Trim();
        var now = _timeProvider.GetUtcNow();
        var evidence = new RobotIdentitySuggestionEvidence(source, field, normalizedCandidate, now);
        if (repository is not null)
        {
            repository.Observe(device.DeviceId, normalizedCandidate, evidence);
            return;
        }

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
                state = new CandidateState(normalizedCandidate, now);
                deviceCandidates[normalizedCandidate] = state;
            }

            if (state.ObservationCount < int.MaxValue) state.ObservationCount++;
            state.LastObservedUtc = now;
            if (!state.Evidence.Any(item =>
                    item.Source.Equals(evidence.Source, StringComparison.OrdinalIgnoreCase) &&
                    item.Field.Equals(evidence.Field, StringComparison.OrdinalIgnoreCase) &&
                    item.Value.Equals(evidence.Value, StringComparison.OrdinalIgnoreCase)))
            {
                state.Evidence.Add(evidence);
                if (state.Evidence.Count > MaxEvidencePerCandidate)
                    state.Evidence.RemoveAt(0);
            }

            if (deviceCandidates.Count > MaxCandidatesPerDevice)
            {
                var weakest = deviceCandidates
                    .OrderBy(pair => pair.Value.ObservationCount)
                    .ThenBy(pair => pair.Value.LastObservedUtc)
                    .ThenByDescending(pair => pair.Value.ProposedRobotId, StringComparer.OrdinalIgnoreCase)
                    .First();
                deviceCandidates.Remove(weakest.Key);
            }
        }
    }

    public RobotIdentitySuggestion? GetSuggestion(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        var device = ResolveDevice(deviceId);
        if (device is null) return null;

        RobotIdentitySuggestionCandidate? best;
        if (repository is not null)
        {
            best = repository.GetBest(device.DeviceId);
        }
        else lock (_syncRoot)
        {
            PurgeExpiredLocked(_timeProvider.GetUtcNow());
            if (!_candidates.TryGetValue(device.DeviceId, out var deviceCandidates)) return null;

            foreach (var stale in deviceCandidates
                         .Where(pair => MatchesCurrentIdentity(device, pair.Key))
                         .Select(pair => pair.Key)
                         .ToArray())
                deviceCandidates.Remove(stale);

            var localBest = deviceCandidates.Values
                .OrderByDescending(candidate => candidate.ObservationCount)
                .ThenByDescending(candidate => candidate.LastObservedUtc)
                .ThenBy(candidate => candidate.ProposedRobotId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            best = localBest is null
                ? null
                : new RobotIdentitySuggestionCandidate(localBest.ProposedRobotId, localBest.ObservationCount,
                    localBest.FirstObservedUtc, localBest.LastObservedUtc, localBest.Evidence.ToArray());
        }
        if (best is null) return null;
        if (MatchesCurrentIdentity(device, best.ProposedRobotId))
        {
            repository?.Dismiss(device.DeviceId, best.ProposedRobotId);
            return null;
        }

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
            best.Evidence);
    }

    public void Dismiss(string? deviceId, string? proposedRobotId = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        if (repository is not null)
        {
            repository.Dismiss(deviceId.Trim(), proposedRobotId?.Trim());
            return;
        }
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

public interface IRobotIdentitySuggestionRepository
{
    void Observe(string deviceId, string proposedRobotId, RobotIdentitySuggestionEvidence evidence);
    RobotIdentitySuggestionCandidate? GetBest(string deviceId);
    void Dismiss(string deviceId, string? proposedRobotId = null);
}

public sealed record RobotIdentitySuggestionCandidate(
    string ProposedRobotId,
    int ObservationCount,
    DateTimeOffset FirstObservedUtc,
    DateTimeOffset LastObservedUtc,
    IReadOnlyList<RobotIdentitySuggestionEvidence> Evidence);

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
        var healthPayloadCandidates = ExtractHealthPayloadCandidates(json);
        if (!CandidateFields.Any(field => json.Contains(field, StringComparison.OrdinalIgnoreCase)) &&
            !json.Contains("\"jibo\"", StringComparison.OrdinalIgnoreCase) &&
            !json.Contains("\"serial_number\"", StringComparison.OrdinalIgnoreCase))
            return syslogCandidates.Concat(healthPayloadCandidates).ToArray();

        try
        {
            using var document = JsonDocument.Parse(json);
            var candidates = new List<RobotIdentityCandidate>(syslogCandidates.Count + healthPayloadCandidates.Count);
            candidates.AddRange(syslogCandidates);
            candidates.AddRange(healthPayloadCandidates);
            Visit(document.RootElement, string.Empty, candidates);
            return candidates
                .Where(candidate => RobotIdentitySuggestionStore.IsSafeIdentityName(candidate.Value))
                .DistinctBy(candidate => $"{candidate.Field}\0{candidate.Value}", StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return syslogCandidates.Concat(healthPayloadCandidates)
                .DistinctBy(candidate => $"{candidate.Field}\0{candidate.Value}", StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>
    /// Reads serial numbers from a bounded robot-log prefix. A serial is evidence for matching an
    /// existing verified registration; this method never treats a log as serial verification.
    /// </summary>
    public static IReadOnlyList<string> ExtractSerialNumbers(string? text) => ReadHealthHeaders(text).Serials;

    // Stream only complete top-level string fields. This accepts NDJSON and a
    // bounded prefix of a large health object without guessing across objects,
    // nested components, or JSON-escaped log messages.
    private static (string[] Names, string[] Serials) ReadHealthHeaders(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ([], []);
        var bytes = System.Text.Encoding.UTF8.GetBytes(text[..Math.Min(text.Length, 256 * 1024)]);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: false,
            new JsonReaderState(new JsonReaderOptions { AllowMultipleValues = true }));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? property = null;
        void FinishRoot()
        {
            if (rootSerials.Count > 0)
            {
                names.UnionWith(rootNames);
                serials.UnionWith(rootSerials);
            }
            rootNames.Clear();
            rootSerials.Clear();
        }
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 0)
                {
                    FinishRoot();
                    property = null;
                }
                else if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
                    property = reader.GetString();
                else if (reader.TokenType == JsonTokenType.String && reader.CurrentDepth == 1)
                {
                    var value = reader.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value) && value.Length <= 120)
                    {
                        if (string.Equals(property, "name", StringComparison.OrdinalIgnoreCase)) rootNames.Add(value);
                        if (string.Equals(property, "serial_number", StringComparison.OrdinalIgnoreCase)) rootSerials.Add(value);
                    }
                    property = null;
                }
                else
                {
                    property = null;
                    if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0) FinishRoot();
                }
            }
        }
        catch (JsonException)
        {
            // Retain already parsed header strings when the sampled tail is incomplete.
        }
        FinishRoot();
        return (names.ToArray(), serials.ToArray());
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

    private static IReadOnlyList<RobotIdentityCandidate> ExtractHealthPayloadCandidates(string text) =>
        ReadHealthHeaders(text).Names
            .Where(RobotIdentitySuggestionStore.IsSafeIdentityName)
            .Select(name => new RobotIdentityCandidate("name", name))
            .ToArray();

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
