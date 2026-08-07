using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Application;

/// <summary>
/// Dump/stock S6/S9 + SyncManager _isLoopGood contract for Loop.List / LoopUpdated.
/// </summary>
public sealed class LoopListGoldenContractTests
{
    [Fact]
    public async Task LoopList_SatisfiesDumpIsLoopGood_AndStockMemberRequiredFields()
    {
        var store = new InMemoryCloudStateStore();
        var ghost = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        store.AddLoop(null, null, "Air-Degree-Lunch-Canvas", "BOJW-1000-0017-1009-0021");
        store.AddLoopMember(ghost.LoopId, null, null, "Intro", "Person", "female", null, false, "member");

        var service = new JiboCloudProtocolService(store, authHandler: new CloudAuthProtocolHandler(store));
        var result = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Loop_20160324",
            Operation = "List",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal(200, result.StatusCode);
        using var payload = JsonDocument.Parse(result.BodyText);
        var loops = payload.RootElement.EnumerateArray().ToArray();
        Assert.Single(loops);

        var loop = loops[0];
        Assert.Equal(ghost.LoopId, loop.GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(loop.GetProperty("owner").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(loop.GetProperty("robot").GetString()));

        var members = loop.GetProperty("members").EnumerateArray().ToArray();
        Assert.NotEmpty(members);

        var accountIds = members
            .Select(member => member.TryGetProperty("accountId", out var accountId) ? accountId.GetString() : null)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(loop.GetProperty("owner").GetString()!, accountIds);
        Assert.Contains(loop.GetProperty("robot").GetString()!, accountIds);

        foreach (var member in members)
        {
            Assert.False(string.IsNullOrWhiteSpace(member.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(member.GetProperty("loopId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(member.GetProperty("status").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(member.GetProperty("type").GetString()));
            Assert.Equal("accepted", member.GetProperty("status").GetString());
        }

        Assert.Contains(members, member =>
            member.GetProperty("type").GetString() == "member" &&
            member.GetProperty("account").GetProperty("firstName").GetString() == "Intro");

        var robotMember = members.Single(member => member.GetProperty("type").GetString() == "robot");
        Assert.Equal(loop.GetProperty("robot").GetString(), robotMember.GetProperty("accountId").GetString());
        Assert.True(
            robotMember.GetProperty("account").GetProperty("firstName").ValueKind is JsonValueKind.Null
                or JsonValueKind.Undefined ||
            string.IsNullOrWhiteSpace(robotMember.GetProperty("account").GetProperty("firstName").GetString()));
    }

    [Fact]
    public async Task LoopUpdated_IncludesFullRosterForSyncManagerRelist()
    {
        var store = new InMemoryCloudStateStore();
        var loop = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        store.AddLoopMember(loop.LoopId, null, null, "Synced", "Person", "male", null, false, "member");

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

        var invite = await service.DispatchAsync(new ProtocolEnvelope
        {
            ServicePrefix = "Loop_20160324",
            Operation = "InviteMember",
            BodyText = $$"""{"loopId":"{{loop.LoopId}}","firstName":"Jordan","lastName":"Lee"}"""
        });
        Assert.Equal(200, invite.StatusCode);
        Assert.NotNull(socket.LastPayload);

        var loopPayload = socket.LastPayload!.Value.GetProperty("payload").GetProperty("payload");
        Assert.Equal(loop.LoopId, loopPayload.GetProperty("id").GetString());
        var members = loopPayload.GetProperty("members").EnumerateArray().ToArray();
        Assert.Contains(members, member => member.GetProperty("type").GetString() == "robot");
        Assert.Contains(members, member =>
            member.GetProperty("account").GetProperty("firstName").GetString() == "Jordan" &&
            member.GetProperty("status").GetString() == "accepted");
    }

    [Fact]
    public async Task ListMembers_ScopesToCallerAndHonorsStatusList()
    {
        var store = new InMemoryCloudStateStore();
        var ghost = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        store.AddLoop(null, null, "Air-Degree-Lunch-Canvas", "BOJW-1000-0017-1009-0021");
        store.AddLoopMember(ghost.LoopId, null, null, "Keep", "Me", "unknown", null, false, "member");

        var service = new JiboCloudProtocolService(store, authHandler: new CloudAuthProtocolHandler(store));
        var result = await service.DispatchAsync(new ProtocolEnvelope
        {
            ServicePrefix = "Loop_20160324",
            Operation = "ListMembers",
            DeviceId = "Ghost-Instance-Onion-Silk",
            BodyText = """{"statusList":["accepted"]}"""
        });

        Assert.Equal(200, result.StatusCode);
        using var payload = JsonDocument.Parse(result.BodyText);
        var members = payload.RootElement.EnumerateArray().ToArray();
        Assert.DoesNotContain(members, member => member.GetProperty("type").GetString() == "robot");
        Assert.Contains(members, member =>
            member.GetProperty("account").GetProperty("firstName").GetString() == "Keep");
        Assert.All(members, member => Assert.Equal("accepted", member.GetProperty("status").GetString()));
    }

    private sealed class CapturingWebSocket : System.Net.WebSockets.WebSocket
    {
        public JsonElement? LastPayload { get; private set; }
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
            using var document = JsonDocument.Parse(buffer.AsMemory());
            LastPayload = document.RootElement.Clone();
            return Task.CompletedTask;
        }
    }
}
