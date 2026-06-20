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

    [Fact]
    public void RegisterPairedConnection_DoesNotCreatePendingVerificationCode()
    {
        var registry = new HomeAssistantConnectionRegistry();
        using var socket = new FakeWebSocket();

        registry.RegisterPairedConnection("ha-instance-1", socket);

        Assert.Null(registry.TryGetPendingByCode("ABCDEF"));
    }

    [Fact]
    public async Task SendCommandAsync_SendsCommandPayload()
    {
        var registry = new HomeAssistantConnectionRegistry();
        using var socket = new CapturingWebSocket();
        registry.RegisterConnection("ha-instance-1", socket);

        var sent = await registry.SendCommandAsync(
            "ha-instance-1",
            "lights_off_named",
            new Dictionary<string, string> { ["targetName"] = "zanes" });

        Assert.True(sent);
        Assert.NotNull(socket.LastPayload);
        Assert.Equal("command", socket.LastPayload!.Value.GetProperty("type").GetString());
        Assert.Equal("lights_off_named", socket.LastPayload.Value.GetProperty("command").GetString());
        Assert.Equal("zanes", socket.LastPayload.Value.GetProperty("targetName").GetString());
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
