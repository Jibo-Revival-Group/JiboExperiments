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
