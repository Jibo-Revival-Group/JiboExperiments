using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class HomeAssistantConnectionRegistryTests
{
    [Fact]
    public void TryGetPendingByCode_ReturnsRegistration()
    {
        var registry = new HomeAssistantConnectionRegistry();
        using var socket = new FakeWebSocket();

        var pending = registry.RegisterConnection("ha-instance-1", socket);

        var lookup = registry.TryGetPendingByCode(pending.Code);

        Assert.NotNull(lookup);
        Assert.Equal("ha-instance-1", lookup.InstanceId);
    }

    private sealed class FakeWebSocket : System.Net.WebSockets.WebSocket
    {
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
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
