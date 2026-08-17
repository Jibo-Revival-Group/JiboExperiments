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
}
