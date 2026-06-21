using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class HomeAssistantCommandServiceTests
{
    [Fact]
    public async Task TryDispatchLightCommandAsync_SendsRoomOffCommand_WhenJiboIsLinked()
    {
        var (service, socket) = CreateLinkedService();

        var dispatched = await service.TryDispatchLightCommandAsync(new TurnContext
        {
            DeviceId = "Ghost-Instance-Onion-Silk",
            NormalizedTranscript = "turn off the lights"
        }, "ha_lights_off");

        Assert.True(dispatched);
        Assert.NotNull(socket.LastPayload);
        Assert.Equal("command", socket.LastPayload!.Value.GetProperty("type").GetString());
        Assert.Equal("lights_off_current_room", socket.LastPayload.Value.GetProperty("command").GetString());
    }

    [Fact]
    public async Task TryDispatchLightCommandAsync_SendsRoomOnCommand_WhenJiboIsLinked()
    {
        var (service, socket) = CreateLinkedService();

        var dispatched = await service.TryDispatchLightCommandAsync(new TurnContext
        {
            DeviceId = "Ghost-Instance-Onion-Silk",
            NormalizedTranscript = "turn on the lights"
        }, "ha_lights_on");

        Assert.True(dispatched);
        Assert.Equal("lights_on_current_room", socket.LastPayload!.Value.GetProperty("command").GetString());
    }

    [Fact]
    public async Task TryDispatchLightCommandAsync_SendsNamedOffCommand_WithTargetName()
    {
        var (service, socket) = CreateLinkedService();

        var dispatched = await service.TryDispatchLightCommandAsync(new TurnContext
        {
            DeviceId = "Ghost-Instance-Onion-Silk",
            NormalizedTranscript = "turn off zanes light"
        }, "ha_lights_off");

        Assert.True(dispatched);
        Assert.Equal("lights_off_named", socket.LastPayload!.Value.GetProperty("command").GetString());
        Assert.Equal("zanes", socket.LastPayload.Value.GetProperty("targetName").GetString());
    }

    [Fact]
    public async Task TryDispatchLightCommandAsync_ReturnsFalse_WhenNoLinkExists()
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-ha-cmd-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var integrationStore = new InMemoryUserIntegrationStore(snapshotStore);
        var registry = new HomeAssistantConnectionRegistry();
        var cloudStateStore = new InMemoryCloudStateStore();

        var service = new HomeAssistantCommandService(integrationStore, registry, cloudStateStore);
        var dispatched = await service.TryDispatchLightCommandAsync(new TurnContext
        {
            DeviceId = "Ghost-Instance-Onion-Silk",
            NormalizedTranscript = "turn off the lights"
        }, "ha_lights_off");

        Assert.False(dispatched);
    }

    [Fact]
    public async Task TryDispatchLightCommandAsync_FallsBackToRoomCommand_WhenTranscriptDoesNotReparse()
    {
        var (service, socket) = CreateLinkedService();

        var dispatched = await service.TryDispatchLightCommandAsync(new TurnContext
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            NormalizedTranscript = "turn off the lights please"
        }, "ha_lights_off");

        Assert.True(dispatched);
        Assert.Equal("lights_off_current_room", socket.LastPayload!.Value.GetProperty("command").GetString());
    }

    [Fact]
    public async Task TryDispatchClimateCommandAsync_SendsRoomSetTemperatureCommand_WhenJiboIsLinked()
    {
        var (service, socket) = CreateLinkedService();

        var dispatched = await service.TryDispatchClimateCommandAsync(new TurnContext
        {
            DeviceId = "Ghost-Instance-Onion-Silk",
            NormalizedTranscript = "set the temperature to 69"
        }, "ha_climate_set_temp");

        Assert.True(dispatched);
        Assert.NotNull(socket.LastPayload);
        Assert.Equal("climate_set_temperature_current_room", socket.LastPayload!.Value.GetProperty("command").GetString());
        Assert.Equal("69", socket.LastPayload.Value.GetProperty("temperature").GetString());
    }

    [Fact]
    public async Task TryDispatchClimateCommandAsync_SendsNamedSetTemperatureCommand_WithTargetName()
    {
        var (service, socket) = CreateLinkedService();

        var dispatched = await service.TryDispatchClimateCommandAsync(new TurnContext
        {
            DeviceId = "Ghost-Instance-Onion-Silk",
            NormalizedTranscript = "set the bedroom thermostat to 72"
        }, "ha_climate_set_temp");

        Assert.True(dispatched);
        Assert.Equal("climate_set_temperature_named", socket.LastPayload!.Value.GetProperty("command").GetString());
        Assert.Equal("bedroom", socket.LastPayload.Value.GetProperty("targetName").GetString());
        Assert.Equal("72", socket.LastPayload.Value.GetProperty("temperature").GetString());
    }

    [Fact]
    public async Task TryDispatchClimateCommandAsync_SendsCoolDownCommand_WithDelta()
    {
        var (service, socket) = CreateLinkedService();

        var dispatched = await service.TryDispatchClimateCommandAsync(new TurnContext
        {
            DeviceId = "Ghost-Instance-Onion-Silk",
            NormalizedTranscript = "it's hot in here"
        }, "ha_climate_cool_down");

        Assert.True(dispatched);
        Assert.Equal("climate_cool_down_current_room", socket.LastPayload!.Value.GetProperty("command").GetString());
        Assert.Equal("2", socket.LastPayload.Value.GetProperty("delta").GetString());
    }

    [Fact]
    public async Task TryDispatchClimateCommandAsync_SendsWarmUpCommand_WithDelta()
    {
        var (service, socket) = CreateLinkedService();

        var dispatched = await service.TryDispatchClimateCommandAsync(new TurnContext
        {
            DeviceId = "Ghost-Instance-Onion-Silk",
            NormalizedTranscript = "it's cold in here"
        }, "ha_climate_warm_up");

        Assert.True(dispatched);
        Assert.Equal("climate_warm_up_current_room", socket.LastPayload!.Value.GetProperty("command").GetString());
        Assert.Equal("2", socket.LastPayload.Value.GetProperty("delta").GetString());
    }

    [Fact]
    public void IsInstanceConnected_ReturnsFalse_WhenSocketNotRegistered()
    {
        var registry = new HomeAssistantConnectionRegistry();
        Assert.False(registry.IsInstanceConnected("missing-instance"));
    }

    private static (HomeAssistantCommandService Service, CapturingWebSocket Socket) CreateLinkedService()
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-ha-cmd-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var integrationStore = new InMemoryUserIntegrationStore(snapshotStore);
        integrationStore.AddHomeAssistantLink(
            "BOJW-1000-0017-0820-0020",
            "Ghost-Instance-Onion-Silk",
            "ha-instance-1");

        var registry = new HomeAssistantConnectionRegistry();
        var socket = new CapturingWebSocket();
        registry.RegisterConnection("ha-instance-1", socket);

        var cloudStateStore = new InMemoryCloudStateStore();
        cloudStateStore.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = "Test Robot"
        });

        var service = new HomeAssistantCommandService(integrationStore, registry, cloudStateStore);
        return (service, socket);
    }

    private sealed class CapturingWebSocket : System.Net.WebSockets.WebSocket
    {
        public System.Text.Json.JsonElement? LastPayload { get; private set; }

        public override System.Net.WebSockets.WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override System.Net.WebSockets.WebSocketState State => System.Net.WebSockets.WebSocketState.Open;
        public override string? SubProtocol => null;

        public override void Abort()
        {
        }

        public override Task CloseAsync(
            System.Net.WebSockets.WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(
            System.Net.WebSockets.WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose()
        {
        }

        public override Task<System.Net.WebSockets.WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            System.Net.WebSockets.WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            using var document = System.Text.Json.JsonDocument.Parse(buffer.Array!.AsMemory(buffer.Offset, buffer.Count));
            LastPayload = document.RootElement.Clone();
            return Task.CompletedTask;
        }
    }
}
