using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Application;

public sealed class RobotIdentitySuggestionStoreTests
{
    [Fact]
    public void Observe_CachesOnlyMismatchedWireIdentityCandidates()
    {
        var stateStore = new InMemoryCloudStateStore();
        stateStore.UpsertDevice(new DeviceRegistration
        {
            DeviceId = "observed-device-001",
            RobotId = "robot-observed-device-001",
            FriendlyName = "OpenJibo Registered Robot"
        });
        var suggestions = new RobotIdentitySuggestionStore(stateStore);

        suggestions.Observe("observed-device-001", "Alpha-Beta-Dodger-Quirk",
            "websocket-context", "data.runtime.loop.jibo.id");
        suggestions.Observe("observed-device-001", "Alpha-Beta-Dodger-Quirk",
            "http:Loop.List", "robotFriendlyId");

        var suggestion = suggestions.GetSuggestion("observed-device-001");
        Assert.NotNull(suggestion);
        Assert.Equal("Alpha-Beta-Dodger-Quirk", suggestion.ProposedRobotId);
        Assert.Equal(2, suggestion.ObservationCount);
        Assert.Equal(2, suggestion.Evidence.Count);

        suggestions.Observe("observed-device-001", "observed-device-001",
            "test", "robotId");
        Assert.Equal("Alpha-Beta-Dodger-Quirk",
            suggestions.GetSuggestion("observed-device-001")!.ProposedRobotId);
    }

    [Fact]
    public void Extract_UsesRobotFieldsButDoesNotTreatHouseholdLoopIdAsRobotIdentity()
    {
        var candidates = RobotIdentityCandidateExtractor.Extract(
            """{"data":{"runtime":{"loop":{"loopId":"Household-Loop-Not-Robot","jibo":{"id":"Alpha-Beta-Dodger-Quirk"}}}}}""");

        var candidate = Assert.Single(candidates);
        Assert.Equal("data.runtime.loop.jibo.id", candidate.Field);
        Assert.Equal("Alpha-Beta-Dodger-Quirk", candidate.Value);
    }

    [Fact]
    public void Extract_ReadsRobotNameFromHealthPayloadWithSerialEvidence()
    {
        var candidates = RobotIdentityCandidateExtractor.Extract(
            """{"system_clock":123,"name":"Coral-Watt-Serrano-Woven","serial_number":"BOJW-1000-0017-0815-0075","health":[]}""");

        var candidate = Assert.Single(candidates);
        Assert.Equal("name", candidate.Field);
        Assert.Equal("Coral-Watt-Serrano-Woven", candidate.Value);
    }

    [Fact]
    public void Extract_ReadsHealthHeaderAndSerialFromNdjsonPrefix()
    {
        const string log = "{\"system_clock\":1,\"name\":\"Black-Byte-Cookie-Crinkle\",\"serial_number\":\"BOJB-1000-0017-0630-0018\",\"health\":[]}\n{\"event\":\"later\"}";

        var candidate = Assert.Single(RobotIdentityCandidateExtractor.Extract(log));
        var serial = Assert.Single(RobotIdentityCandidateExtractor.ExtractSerialNumbers(log));

        Assert.Equal("Black-Byte-Cookie-Crinkle", candidate.Value);
        Assert.Equal("BOJB-1000-0017-0630-0018", serial);
    }
    [Fact]
    public void Extract_ReadsRobotHostnameFromSyslogLines()
    {
        var candidates = RobotIdentityCandidateExtractor.Extract(
            "2026-08-19T12:03:06.479775+02:00 Coral-Watt-Serrano-Woven rsyslogd[-,info]: rsyslogd was HUPed");

        var candidate = Assert.Single(candidates);
        Assert.Equal("syslog.hostname", candidate.Field);
        Assert.Equal("Coral-Watt-Serrano-Woven", candidate.Value);
    }

    [Fact]
    public void GetSuggestion_ProposesMergeWhenCandidateBelongsToAnotherRobot()
    {
        var stateStore = new InMemoryCloudStateStore();
        stateStore.UpsertDevice(new DeviceRegistration
        {
            DeviceId = "observed-device-001",
            RobotId = "robot-observed-device-001",
            FriendlyName = "OpenJibo Registered Robot"
        });
        stateStore.UpsertDevice(new DeviceRegistration
        {
            DeviceId = "canonical-device-001",
            RobotId = "Alpha-Beta-Dodger-Quirk",
            FriendlyName = "Alpha-Beta-Dodger-Quirk"
        });
        var suggestions = new RobotIdentitySuggestionStore(stateStore);

        suggestions.Observe("observed-device-001", "Alpha-Beta-Dodger-Quirk",
            "websocket-context", "data.runtime.loop.jibo.id");

        var suggestion = Assert.IsType<RobotIdentitySuggestion>(
            suggestions.GetSuggestion("observed-device-001"));
        Assert.Equal("merge", suggestion.Action);
        Assert.Equal("canonical-device-001", suggestion.TargetDeviceId);
    }

    [Fact]
    public void GetSuggestion_BreaksExactTiesByProposedRobotIdRegardlessOfInsertionOrder()
    {
        var now = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);

        var first = CreateStore("observed-device-001", now);
        first.Observe("observed-device-001", "Zulu-Beta-Charlie-Delta", "test", "name");
        first.Observe("observed-device-001", "Alpha-Beta-Charlie-Delta", "test", "name");

        var second = CreateStore("observed-device-002", now);
        second.Observe("observed-device-002", "Alpha-Beta-Charlie-Delta", "test", "name");
        second.Observe("observed-device-002", "Zulu-Beta-Charlie-Delta", "test", "name");

        Assert.Equal("Alpha-Beta-Charlie-Delta",
            first.GetSuggestion("observed-device-001")!.ProposedRobotId);
        Assert.Equal("Alpha-Beta-Charlie-Delta",
            second.GetSuggestion("observed-device-002")!.ProposedRobotId);
    }

    [Fact]
    public void Observe_WhenCandidatesTie_EvictsLexicallyLastCandidate()
    {
        var now = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        var expected = new[]
        {
            "Alpha-Beta-Charlie-Delta",
            "Whiskey-Beta-Charlie-Delta",
            "Xray-Beta-Charlie-Delta",
            "Yankee-Beta-Charlie-Delta"
        };
        var candidates = expected.Append("Zulu-Beta-Charlie-Delta").ToArray();

        var forward = CreateStore("observed-device-001", now);
        foreach (var candidate in candidates)
            forward.Observe("observed-device-001", candidate, "test", "name");

        var reverse = CreateStore("observed-device-002", now);
        foreach (var candidate in candidates.Reverse())
            reverse.Observe("observed-device-002", candidate, "test", "name");

        Assert.Equal(expected, DrainSuggestions(forward, "observed-device-001"));
        Assert.Equal(expected, DrainSuggestions(reverse, "observed-device-002"));
    }

    [Fact]
    public void Repository_SharesSuggestionsAcrossStoreInstances()
    {
        var stateStore = new InMemoryCloudStateStore();
        stateStore.UpsertDevice(new DeviceRegistration
        {
            DeviceId = "observed-device-001",
            RobotId = "robot-observed-device-001",
            FriendlyName = "OpenJibo Registered Robot"
        });
        var repository = new SharedSuggestionRepository();
        var writer = new RobotIdentitySuggestionStore(stateStore, repository);
        var reader = new RobotIdentitySuggestionStore(stateStore, repository);

        writer.Observe("observed-device-001", "Alpha-Beta-Dodger-Quirk",
            "websocket-context", "data.runtime.loop.jibo.id");
        writer.Observe("observed-device-001", "Alpha-Beta-Dodger-Quirk",
            "http:Loop.List", "robotFriendlyId");

        var suggestion = Assert.IsType<RobotIdentitySuggestion>(
            reader.GetSuggestion("observed-device-001"));
        Assert.Equal("Alpha-Beta-Dodger-Quirk", suggestion.ProposedRobotId);
        Assert.Equal(2, suggestion.ObservationCount);
        reader.Dismiss("observed-device-001", suggestion.ProposedRobotId);
        Assert.Null(writer.GetSuggestion("observed-device-001"));
    }

    private static RobotIdentitySuggestionStore CreateStore(string deviceId, DateTimeOffset now)
    {
        var stateStore = new InMemoryCloudStateStore();
        stateStore.UpsertDevice(new DeviceRegistration
        {
            DeviceId = deviceId,
            RobotId = $"robot-{deviceId}",
            FriendlyName = "OpenJibo Registered Robot"
        });
        return new RobotIdentitySuggestionStore(stateStore, null, new FixedTimeProvider(now));
    }

    private static IReadOnlyList<string> DrainSuggestions(
        RobotIdentitySuggestionStore suggestions,
        string deviceId)
    {
        var selected = new List<string>();
        while (suggestions.GetSuggestion(deviceId) is { } suggestion)
        {
            selected.Add(suggestion.ProposedRobotId);
            suggestions.Dismiss(deviceId, suggestion.ProposedRobotId);
        }
        return selected;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SharedSuggestionRepository : IRobotIdentitySuggestionRepository
    {
        private readonly Dictionary<string, RobotIdentitySuggestionCandidate> _suggestions =
            new(StringComparer.OrdinalIgnoreCase);

        public void Observe(string deviceId, string proposedRobotId, RobotIdentitySuggestionEvidence evidence)
        {
            if (_suggestions.TryGetValue(deviceId, out var existing))
            {
                _suggestions[deviceId] = existing with
                {
                    ObservationCount = existing.ObservationCount + 1,
                    LastObservedUtc = evidence.ObservedUtc,
                    Evidence = existing.Evidence.Concat([evidence]).ToArray()
                };
                return;
            }

            _suggestions[deviceId] = new RobotIdentitySuggestionCandidate(
                proposedRobotId, 1, evidence.ObservedUtc, evidence.ObservedUtc, [evidence]);
        }

        public RobotIdentitySuggestionCandidate? GetBest(string deviceId) =>
            _suggestions.GetValueOrDefault(deviceId);

        public void Dismiss(string deviceId, string? proposedRobotId = null)
        {
            if (proposedRobotId is null ||
                _suggestions.TryGetValue(deviceId, out var existing) &&
                existing.ProposedRobotId.Equals(proposedRobotId, StringComparison.OrdinalIgnoreCase))
                _suggestions.Remove(deviceId);
        }
    }
}
