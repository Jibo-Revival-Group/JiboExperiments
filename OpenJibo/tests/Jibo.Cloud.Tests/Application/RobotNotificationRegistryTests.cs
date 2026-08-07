using System.Net.WebSockets;
using System.Text.Json;
using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class RobotNotificationRegistryTests
{
    [Fact]
    public async Task PushLoopUpdatedAsync_SendsDoubleWrappedStockFrame()
    {
        var registry = new RobotNotificationRegistry();
        using var socket = new CapturingWebSocket();
        registry.Register(["Ghost-Instance-Onion-Silk", "token-robot-1"], socket);

        var pushed = await registry.PushLoopUpdatedAsync(
            ["Ghost-Instance-Onion-Silk"],
            new
            {
                id = "loop-1",
                name = "Household",
                members = Array.Empty<object>(),
                eventKey = "LoopUpdated"
            });

        Assert.Equal(1, pushed);
        Assert.NotNull(socket.LastPayload);
        var outer = socket.LastPayload!.Value;
        Assert.Equal("LoopUpdated", outer.GetProperty("payload").GetProperty("name").GetString());
        Assert.Equal("loop-1", outer.GetProperty("payload").GetProperty("payload").GetProperty("id").GetString());
        Assert.Equal(
            "LoopUpdated",
            outer.GetProperty("payload").GetProperty("payload").GetProperty("eventKey").GetString());
    }

    [Fact]
    public async Task PushLoopUpdatedAsync_SkipsWhenKeysDoNotIntersect()
    {
        var registry = new RobotNotificationRegistry();
        using var socket = new CapturingWebSocket();
        registry.Register(["robot-a"], socket);

        var pushed = await registry.PushLoopUpdatedAsync(["robot-b"], new { id = "loop-1" });

        Assert.Equal(0, pushed);
        Assert.Null(socket.LastPayload);
    }

    [Fact]
    public async Task PushLoopUpdatedAsync_SkipsClosedSockets()
    {
        var registry = new RobotNotificationRegistry();
        using var socket = new CapturingWebSocket { ForceClosed = true };
        registry.Register(["robot-a"], socket);

        var pushed = await registry.PushLoopUpdatedAsync(["robot-a"], new { id = "loop-1" });

        Assert.Equal(0, pushed);
        Assert.Null(socket.LastPayload);
    }

    [Fact]
    public async Task Remove_UnregistersSocket()
    {
        var registry = new RobotNotificationRegistry();
        using var socket = new CapturingWebSocket();
        registry.Register(["robot-a"], socket);
        registry.Remove(socket);

        var pushed = await registry.PushLoopUpdatedAsync(["robot-a"], new { id = "loop-1" });

        Assert.Equal(0, pushed);
        Assert.Null(socket.LastPayload);
    }

    [Fact]
    public async Task PushLoopUpdatedAsync_QueuesPending_WhenNoLiveSocket()
    {
        var pending = new RobotPendingNotificationStore();
        var registry = new RobotNotificationRegistry(pending);

        var pushed = await registry.PushLoopUpdatedAsync(["robot-a"], new { id = "loop-1", eventKey = "LoopUpdated" });

        Assert.Equal(0, pushed);
        Assert.Equal(1, registry.PendingCount);

        using var socket = new CapturingWebSocket();
        var drained = await registry.DrainPendingAsync(["robot-a"], socket);

        Assert.Equal(1, drained);
        Assert.Equal(0, registry.PendingCount);
        Assert.NotNull(socket.LastPayload);
        Assert.Equal("LoopUpdated", socket.LastPayload!.Value.GetProperty("payload").GetProperty("name").GetString());
    }

    [Fact]
    public async Task PushLoopUpdatedAsync_CoalescesPendingByRobotOverlap()
    {
        var pending = new RobotPendingNotificationStore();
        var registry = new RobotNotificationRegistry(pending);

        await registry.PushLoopUpdatedAsync(["robot-a"], new { id = "loop-old", eventKey = "LoopUpdated" });
        await registry.PushLoopUpdatedAsync(["robot-a"], new { id = "loop-new", eventKey = "LoopUpdated" });

        Assert.Equal(1, registry.PendingCount);

        using var socket = new CapturingWebSocket();
        var drained = await registry.DrainPendingAsync(["robot-a"], socket);

        Assert.Equal(1, drained);
        Assert.Equal(
            "loop-new",
            socket.LastPayload!.Value.GetProperty("payload").GetProperty("payload").GetProperty("id").GetString());
    }

    [Fact]
    public async Task UpdateKeysAsync_ExpandsKeysAndDrainsPending()
    {
        var pending = new RobotPendingNotificationStore();
        var registry = new RobotNotificationRegistry(pending);
        using var socket = new CapturingWebSocket();
        registry.Register(["friendly-only"], socket);

        await registry.PushLoopUpdatedAsync(["kb-hex-id"], new { id = "loop-1", eventKey = "LoopUpdated" });
        Assert.Equal(1, registry.PendingCount);
        Assert.Null(socket.LastPayload);

        var drained = await registry.UpdateKeysAsync(socket, ["friendly-only", "kb-hex-id"]);
        Assert.Equal(1, drained);
        Assert.Equal(0, registry.PendingCount);
        Assert.Equal(
            "LoopUpdated",
            socket.LastPayload!.Value.GetProperty("payload").GetProperty("name").GetString());
    }

    private sealed class CapturingWebSocket : WebSocket
    {
        public JsonElement? LastPayload { get; private set; }
        public bool ForceClosed { get; set; }

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => ForceClosed ? WebSocketState.Closed : WebSocketState.Open;
        public override string? SubProtocol => null;

        public override void Abort()
        {
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(buffer.AsMemory());
            LastPayload = document.RootElement.Clone();
            return Task.CompletedTask;
        }
    }
}
