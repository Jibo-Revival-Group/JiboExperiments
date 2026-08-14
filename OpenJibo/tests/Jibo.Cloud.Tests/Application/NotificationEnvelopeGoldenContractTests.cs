using System.Net.WebSockets;
using System.Text.Json;
using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

/// <summary>
/// Locks the notification frame against the robot handler recovered from a stock OS 1.9
/// dump (<c>jibo-client-framework/src/NotificationsDispatcher.ts</c>):
///
/// <code>
/// interface NotificationMessage { _id:string; skillId:string; payload:Payload; created:string; }
/// interface Payload { name:string; payload:any; }
/// </code>
///
/// The dispatcher drops anything without a truthy <c>payload.name</c> and emits that name
/// as an EventEmitter event, which is what SyncManager subscribes to. See
/// docs/loop-syncmanager-contract.md.
/// </summary>
public sealed class NotificationEnvelopeGoldenContractTests
{
    [Fact]
    public async Task PushLoopUpdatedAsync_EmitsTheEnvelopeShapeTheRobotDispatcherAccepts()
    {
        var registry = new RobotNotificationRegistry();
        using var socket = new CapturingWebSocket();
        registry.Register(["Air-Degree-Lunch-Canvas"], socket);

        var pushed = await registry.PushLoopUpdatedAsync(
            ["Air-Degree-Lunch-Canvas"],
            new { loopId = "loop-air-degree-lunch-canvas" });

        Assert.Equal(1, pushed);
        var frame = socket.LastPayload!.Value;

        Assert.Equal(
            ["_id", "created", "skillId", "payload"],
            frame.EnumerateObject().Select(property => property.Name).ToArray());

        Assert.False(string.IsNullOrWhiteSpace(frame.GetProperty("_id").GetString()));
        Assert.Equal("-1", frame.GetProperty("skillId").GetString());
        Assert.True(frame.GetProperty("created").GetInt64() > 0);

        var payload = frame.GetProperty("payload");
        Assert.Equal(
            ["name", "payload"],
            payload.EnumerateObject().Select(property => property.Name).ToArray());

        // The dispatcher gate is `message.payload.name` being truthy, and it emits that
        // name verbatim — `error` would hit EventEmitter's throw-on-unhandled rule.
        var name = payload.GetProperty("name").GetString();
        Assert.Equal("LoopUpdated", name);
        Assert.NotEqual("error", name);

        Assert.Equal(
            "loop-air-degree-lunch-canvas",
            payload.GetProperty("payload").GetProperty("loopId").GetString());
    }

    [Fact]
    public async Task PushLoopUpdatedAsync_RecordsTheFrameForDiagnostics()
    {
        var registry = new RobotNotificationRegistry();
        using var socket = new CapturingWebSocket();
        registry.Register(["Air-Degree-Lunch-Canvas"], socket);

        await registry.PushLoopUpdatedAsync(["Air-Degree-Lunch-Canvas"], new { loopId = "loop-x" });

        var attempt = registry.LastPushAttempt;
        Assert.NotNull(attempt);
        Assert.Equal("LoopUpdated", attempt!.Name);
        Assert.Equal(1, attempt.PushedCount);
        Assert.Contains("\"LoopUpdated\"", attempt.FramePreview, StringComparison.Ordinal);
        Assert.Contains("Air-Degree-Lunch-Canvas", attempt.TargetKeys);
    }

    [Fact]
    public async Task PushLoopUpdatedAsync_KeyMissRecordsBothKeySets()
    {
        var registry = new RobotNotificationRegistry();
        using var socket = new CapturingWebSocket();
        registry.Register(["some-other-robot"], socket);

        var pushed = await registry.PushLoopUpdatedAsync(["Air-Degree-Lunch-Canvas"], new { loopId = "loop-x" });

        Assert.Equal(0, pushed);
        var attempt = registry.LastPushAttempt;
        Assert.NotNull(attempt);
        Assert.Equal(0, attempt!.PushedCount);
        Assert.Contains("Air-Degree-Lunch-Canvas", attempt.TargetKeys);
        Assert.Contains(attempt.OpenConnectionKeys, keys => keys.Contains("some-other-robot"));
    }

    private sealed class CapturingWebSocket : WebSocket
    {
        public JsonElement? LastPayload { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
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
