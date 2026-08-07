using System.Net.WebSockets;
using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class JiboCloudProtocolLoopUpdatedTests
{
    [Fact]
    public async Task DispatchAsync_UpdateMember_PushesLoopUpdatedToLiveSocket()
    {
        var store = new InMemoryCloudStateStore();
        var loop = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        var member = store.AddLoopMember(
            loop.LoopId,
            null,
            null,
            "Alex",
            "Tester",
            "unknown",
            null,
            false,
            "member");

        var pendingStore = new RobotPendingNotificationStore();
        var registry = new RobotNotificationRegistry(pendingStore);
        var pushService = new LoopUpdatedPushService(store, registry, NullLogger<LoopUpdatedPushService>.Instance);
        var service = new JiboCloudProtocolService(
            store,
            authHandler: new CloudAuthProtocolHandler(store),
            robotNotificationRegistry: registry,
            loopUpdatedPushService: pushService);

        using var socket = new CapturingWebSocket();
        registry.Register(["Ghost-Instance-Onion-Silk"], socket);

        var result = await service.DispatchAsync(new ProtocolEnvelope
        {
            Scheme = "http",
            HostName = "192.168.7.105",
            Authority = "192.168.7.105:8765",
            ServicePrefix = "Loop_20160324",
            Operation = "UpdateMember",
            BodyText =
                $$"""{"loopId":"{{loop.LoopId}}","id":"{{member.Id}}","firstName":"Jordan","lastName":"Tester","gender":"male"}"""
        });

        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(socket.LastPayload);
        Assert.Equal(
            "LoopUpdated",
            socket.LastPayload!.Value.GetProperty("payload").GetProperty("name").GetString());

        var updated = store.GetLoopMembers(loop.LoopId).Single(m => m.Id == member.Id);
        Assert.Equal("Jordan", updated.FirstName);
    }

    [Fact]
    public async Task DispatchAsync_InviteAndRemoveMember_MutateLoopAndPushLoopUpdated()
    {
        var store = new InMemoryCloudStateStore();
        var loop = store.AddLoop(null, null, "Air-Degree-Lunch-Canvas", "BOJW-1000-0017-1009-0021");
        var pendingStore = new RobotPendingNotificationStore();
        var registry = new RobotNotificationRegistry(pendingStore);
        var pushService = new LoopUpdatedPushService(store, registry, NullLogger<LoopUpdatedPushService>.Instance);
        var service = new JiboCloudProtocolService(
            store,
            authHandler: new CloudAuthProtocolHandler(store),
            robotNotificationRegistry: registry,
            loopUpdatedPushService: pushService);

        using var socket = new CapturingWebSocket();
        registry.Register(["Air-Degree-Lunch-Canvas"], socket);

        var invite = await service.DispatchAsync(new ProtocolEnvelope
        {
            Scheme = "http",
            Authority = "192.168.7.105:8765",
            ServicePrefix = "Loop_20160324",
            Operation = "InviteMember",
            BodyText =
                $$"""{"loopId":"{{loop.LoopId}}","firstName":"Sam","lastName":"Owner","email":"sam@example.com"}"""
        });
        Assert.Equal(200, invite.StatusCode);
        Assert.Equal("LoopUpdated",
            socket.LastPayload!.Value.GetProperty("payload").GetProperty("name").GetString());

        var invited = store.GetLoopMembers(loop.LoopId)
            .Single(m => string.Equals(m.FirstName, "Sam", StringComparison.OrdinalIgnoreCase));

        var remove = await service.DispatchAsync(new ProtocolEnvelope
        {
            Scheme = "http",
            Authority = "192.168.7.105:8765",
            ServicePrefix = "Loop_20160324",
            Operation = "RemoveMember",
            BodyText = $$"""{"loopId":"{{loop.LoopId}}","id":"{{invited.Id}}"}"""
        });
        Assert.Equal(200, remove.StatusCode);
        Assert.DoesNotContain(store.GetLoopMembers(loop.LoopId), m => m.Id == invited.Id);
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
