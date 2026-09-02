using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jibo.Cloud.Tests.Application;

public sealed class ContextReleasePersistenceTests
{
    [Fact]
    public async Task HandleContextAsync_PersistsGeneralReleaseOntoDevice()
    {
        var store = new InMemoryCloudStateStore();
        var turnService = new WebSocketTurnFinalizationService(
            Mock.Of<IConversationBroker>(),
            Mock.Of<ISttStrategySelector>(),
            Mock.Of<ITurnTelemetrySink>(),
            NullLogger<WebSocketTurnFinalizationService>.Instance,
            cloudStateStore: store);

        var session = store.OpenSession("neo-hub-listen", null, "conn:beam", "192.168.1.10", "/v1/listen");
        Assert.Null(session.DeviceId);

        await turnService.HandleContextAsync(
            session,
            new WebSocketMessageEnvelope
            {
                Text =
                    """{"type":"CONTEXT","data":{"runtime":{"loop":{"loopId":"loop-1","jibo":{"id":"jibo-beam-unit"},"users":[]}},"general":{"release":"BEam.1.1.0"}}}"""
            });

        Assert.Equal("jibo-beam-unit", session.DeviceId);
        Assert.Equal("BEam.1.1.0", session.Metadata["firmwareVersion"]?.ToString());
        var device = store.FindDeviceByFriendlyId("jibo-beam-unit") ??
                     store.GetDevices().FirstOrDefault(d => d.DeviceId == "jibo-beam-unit");
        Assert.NotNull(device);
        Assert.Equal("BEam.1.1.0", device.FirmwareVersion);
    }

    [Fact]
    public async Task HandleContextAsync_RecordsIdentitySuggestionWhileTrafficIsInFlight()
    {
        var store = new InMemoryCloudStateStore();
        store.UpsertDevice(new DeviceRegistration
        {
            DeviceId = "observed-device-001",
            RobotId = "robot-observed-device-001",
            FriendlyName = "OpenJibo Registered Robot"
        });
        var suggestions = new RobotIdentitySuggestionStore(store);
        var turnService = new WebSocketTurnFinalizationService(
            Mock.Of<IConversationBroker>(),
            Mock.Of<ISttStrategySelector>(),
            Mock.Of<ITurnTelemetrySink>(),
            NullLogger<WebSocketTurnFinalizationService>.Instance,
            cloudStateStore: store,
            identitySuggestionStore: suggestions);
        var session = store.OpenSession("neo-hub-listen", "observed-device-001", "conn:identity-test",
            "neohub.openjibo.com", "/v1/listen");

        await turnService.HandleContextAsync(session, new WebSocketMessageEnvelope
        {
            Text =
                """{"type":"CONTEXT","data":{"runtime":{"loop":{"loopId":"household-loop","jibo":{"id":"Alpha-Beta-Dodger-Quirk"},"users":[]}},"general":{"release":"12.10.0"}}}"""
        });

        var suggestion = suggestions.GetSuggestion("observed-device-001");
        Assert.NotNull(suggestion);
        Assert.Equal("Alpha-Beta-Dodger-Quirk", suggestion.ProposedRobotId);
        Assert.Contains(suggestion.Evidence, evidence => evidence.Source == "websocket-context" &&
                                                        evidence.Field.EndsWith("runtime.loop.jibo.id",
                                                            StringComparison.Ordinal));
        Assert.DoesNotContain(store.GetDevices(), device =>
            device.DeviceId.Equals("Alpha-Beta-Dodger-Quirk", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("12.10.0", store.FindDeviceByFriendlyId("observed-device-001")!.FirmwareVersion);
    }

    [Fact]
    public async Task HandleContextAsync_ReusesCanonicalDeviceWhoseRobotIdMatchesContext()
    {
        var store = new InMemoryCloudStateStore();
        store.UpsertDevice(new DeviceRegistration
        {
            DeviceId = "physical-device-001",
            RobotId = "Royal-Current-Sage-Canvas",
            FriendlyName = "Royal-Current-Sage-Canvas",
            RegistrationSource = RobotRegistrationSources.Physical
        });
        var suggestions = new RobotIdentitySuggestionStore(store);
        var turnService = new WebSocketTurnFinalizationService(
            Mock.Of<IConversationBroker>(),
            Mock.Of<ISttStrategySelector>(),
            Mock.Of<ITurnTelemetrySink>(),
            NullLogger<WebSocketTurnFinalizationService>.Instance,
            cloudStateStore: store,
            identitySuggestionStore: suggestions);
        var session = store.OpenSession("neo-hub-listen", "Royal-Current-Sage-Canvas", "conn:canonical-identity",
            "neohub.openjibo.com", "/v1/listen");

        await turnService.HandleContextAsync(session, new WebSocketMessageEnvelope
        {
            Text =
                """{"type":"CONTEXT","data":{"runtime":{"loop":{"loopId":"household-loop","jibo":{"id":"Royal-Current-Sage-Canvas"},"users":[]}},"general":{"release":"12.10.0"}}}"""
        });

        Assert.DoesNotContain(store.GetDevices(), device =>
            device.DeviceId.Equals("Royal-Current-Sage-Canvas", StringComparison.OrdinalIgnoreCase));
        var canonical = Assert.Single(store.GetDevices(), device => device.DeviceId == "physical-device-001");
        Assert.Equal("12.10.0", canonical.FirmwareVersion);
    }
}
