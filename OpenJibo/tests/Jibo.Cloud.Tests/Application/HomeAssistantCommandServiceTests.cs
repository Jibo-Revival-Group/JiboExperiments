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
        });

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
        });

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
        });

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
        });

        Assert.False(dispatched);
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
