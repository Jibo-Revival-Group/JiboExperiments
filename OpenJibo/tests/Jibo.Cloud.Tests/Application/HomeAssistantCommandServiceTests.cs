using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class HomeAssistantCommandServiceTests
{
    [Fact]
    public async Task TryDispatchLightsOffAsync_SendsCommand_WhenJiboIsLinked()
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
        using var socket = new CapturingWebSocket();
        registry.RegisterConnection("ha-instance-1", socket);

        var cloudStateStore = new InMemoryCloudStateStore();
        cloudStateStore.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = "Test Robot"
        });

        var service = new HomeAssistantCommandService(integrationStore, registry, cloudStateStore);
        var dispatched = await service.TryDispatchLightsOffAsync(new TurnContext
        {
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.True(dispatched);
        Assert.NotNull(socket.LastPayload);
        Assert.Equal("command", socket.LastPayload!.Value.GetProperty("type").GetString());
        Assert.Equal("lights_off_current_room", socket.LastPayload.Value.GetProperty("command").GetString());
    }

    [Fact]
    public async Task TryDispatchLightsOffAsync_ReturnsFalse_WhenNoLinkExists()
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-ha-cmd-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var integrationStore = new InMemoryUserIntegrationStore(snapshotStore);
        var registry = new HomeAssistantConnectionRegistry();
        var cloudStateStore = new InMemoryCloudStateStore();

        var service = new HomeAssistantCommandService(integrationStore, registry, cloudStateStore);
        var dispatched = await service.TryDispatchLightsOffAsync(new TurnContext
        {
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.False(dispatched);
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
