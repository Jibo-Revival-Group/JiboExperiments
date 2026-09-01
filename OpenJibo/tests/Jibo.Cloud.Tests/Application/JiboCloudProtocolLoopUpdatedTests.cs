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
    }

    [Fact]
    public async Task DispatchAsync_ListLoops_IncludesRobotMemberMatchingLoopRobotAccountId()
    {
        // Dump-backed stock contract: LoopManager._isLoopGood / _applyLoopChanges
        // require members[].accountId to include loop.owner and loop.robot.
        var store = new InMemoryCloudStateStore();
        var loop = Assert.Single(store.GetLoops());
        store.AddLoopMember(
            loop.LoopId,
            "acct-portal-person",
            null,
            "Pat",
            "Person",
            "unknown",
            null,
            false,
            "member");

        var service = new JiboCloudProtocolService(
            store,
            authHandler: new CloudAuthProtocolHandler(store));

        var result = await service.DispatchAsync(new ProtocolEnvelope
        {
            ServicePrefix = "Loop_20160324",
            Operation = "ListLoops",
            BodyText = "{}"
        });

        Assert.Equal(200, result.StatusCode);
        using var payload = JsonDocument.Parse(result.BodyText);
        var listed = Assert.Single(payload.RootElement.EnumerateArray());
        Assert.Equal(loop.LoopId, listed.GetProperty("id").GetString());
        Assert.Equal(loop.RobotId, listed.GetProperty("robot").GetString());
        var members = listed.GetProperty("members").EnumerateArray().ToArray();
        var robot = Assert.Single(members, m => m.GetProperty("type").GetString() == "robot");
        var owner = Assert.Single(members, m => m.GetProperty("type").GetString() == "owner");
        var person = Assert.Single(members, m => m.GetProperty("account").GetProperty("firstName").GetString() == "Pat");
        Assert.Equal(listed.GetProperty("robot").GetString(), robot.GetProperty("accountId").GetString());
        Assert.Equal(listed.GetProperty("owner").GetString(), owner.GetProperty("accountId").GetString());
        Assert.Equal("acct-portal-person", person.GetProperty("accountId").GetString());
    }

    [Fact]
    public async Task DispatchAsync_UpdateMember_LoopUpdatedPayloadIncludesRobotMember()
    {
        var store = new InMemoryCloudStateStore();
        var loop = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        var member = store.AddLoopMember(
            loop.LoopId,
            "acct-alex",
            null,
            "Alex",
            "Tester",
            "unknown",
            null,
            false,
            "member");

        var registry = new RobotNotificationRegistry();
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
            ServicePrefix = "Loop_20160324",
            Operation = "UpdateMember",
            BodyText =
                $$"""{"loopId":"{{loop.LoopId}}","id":"{{member.Id}}","firstName":"Jordan","lastName":"Tester","gender":"male"}"""
        });

        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(socket.LastPayload);
        var loopPayload = socket.LastPayload!.Value.GetProperty("payload").GetProperty("payload");
        var members = loopPayload.GetProperty("members").EnumerateArray().ToArray();
        Assert.Contains(members, m => m.GetProperty("type").GetString() == "robot");
        Assert.Contains(members, m => m.GetProperty("account").GetProperty("firstName").GetString() == "Jordan");
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
