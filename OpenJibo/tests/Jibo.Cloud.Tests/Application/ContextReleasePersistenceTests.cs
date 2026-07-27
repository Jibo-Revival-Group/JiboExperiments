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
}
