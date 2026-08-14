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
            member.GetProperty("type").GetString() == "outgoing" &&
            member.GetProperty("account").TryGetProperty("firstName", out var firstName) &&
            firstName.GetString() == "Intro" &&
            member.TryGetProperty("firstName", out var flatFirst) &&
            flatFirst.GetString() == "Intro");

        // Stock wire type for the robot member is "outgoing" with an empty account object.
        var robotMember = members.Single(member =>
            member.GetProperty("accountId").GetString() == loop.GetProperty("robot").GetString());
        Assert.Equal("outgoing", robotMember.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, robotMember.GetProperty("account").ValueKind);
        Assert.False(robotMember.GetProperty("account").EnumerateObject().Any());
        Assert.False(robotMember.TryGetProperty("firstName", out _));
    }

    /// <summary>
    /// After a household has real people, the List payload must contain no placeholder
    /// person while still resolving <c>loop.owner</c> to a member — the robot warns about a
    /// missing owner in <c>_isLoopGood</c> but then throws on it in <c>_applyLoopChanges</c>.
    /// </summary>
    [Fact]
    public async Task LoopList_AfterOwnerIsClaimed_HasNoPlaceholderPersonButStillResolvesOwner()
    {
        var store = new InMemoryCloudStateStore();
        var ghost = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        var claimed = store.ClaimSeededOwner(ghost.LoopId, "Zane", "Ricci", "male", null, false);
        Assert.NotNull(claimed);

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
        var loop = payload.RootElement.EnumerateArray().Single();
        var members = loop.GetProperty("members").EnumerateArray().ToArray();

        var accountIds = members
            .Select(member => member.TryGetProperty("accountId", out var accountId) ? accountId.GetString() : null)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.Contains(loop.GetProperty("owner").GetString()!, accountIds);
        Assert.Contains(loop.GetProperty("robot").GetString()!, accountIds);

        // Introductions shows any member with a truthy firstName, so the placeholder must
        // be gone from both the flattened and nested name fields.
        Assert.DoesNotContain(members, member =>
            (member.TryGetProperty("firstName", out var flat) && flat.GetString() == "Jibo") ||
            (member.GetProperty("account").TryGetProperty("firstName", out var nested) &&
             nested.GetString() == "Jibo"));

        var person = Assert.Single(members, member =>
            member.TryGetProperty("firstName", out var first) && first.GetString() == "Zane");
        Assert.Equal(loop.GetProperty("owner").GetString(), person.GetProperty("accountId").GetString());
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
        Assert.Contains(members, member =>
            member.GetProperty("accountId").GetString() == loopPayload.GetProperty("robot").GetString() &&
            member.GetProperty("type").GetString() == "outgoing");
        Assert.Contains(members, member =>
            member.GetProperty("account").TryGetProperty("firstName", out var firstName) &&
            firstName.GetString() == "Jordan" &&
            member.TryGetProperty("firstName", out var flatFirst) &&
            flatFirst.GetString() == "Jordan" &&
            member.GetProperty("status").GetString() == "accepted" &&
            member.GetProperty("type").GetString() == "outgoing");
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
        // ListMembers excludes the robot member (internal type=robot), so no empty-account row.
        Assert.DoesNotContain(members, member =>
            member.GetProperty("account").ValueKind == JsonValueKind.Object &&
            !member.GetProperty("account").EnumerateObject().Any());
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
