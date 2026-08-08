using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jibo.Cloud.Tests.Api;

public sealed class PortalLoopMemberSyncTests
{
    [Fact]
    public async Task PortalLoopMemberAdd_PushesLoopUpdatedOnRegisteredApiSocket()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthorizeAsync(client, factory);

        var registry = factory.Services.GetRequiredService<RobotNotificationRegistry>();
        using var socket = new CapturingWebSocket();
        registry.Register(["Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020"], socket);

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "Synced", lastName = "Person", gender = "female" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var added = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(added.GetProperty("syncedToRobot").GetBoolean());
        Assert.True(added.GetProperty("pushCount").GetInt32() > 0);

        Assert.NotNull(socket.LastPayload);
        var outer = socket.LastPayload!.Value;
        Assert.Equal("LoopUpdated", outer.GetProperty("payload").GetProperty("name").GetString());
        var members = outer.GetProperty("payload").GetProperty("payload").GetProperty("members");
        Assert.Contains(
            members.EnumerateArray(),
            member => member.GetProperty("account").GetProperty("firstName").GetString() == "Synced" &&
                      member.GetProperty("status").GetString() == "accepted");
        Assert.Contains(
            members.EnumerateArray(),
            member => member.GetProperty("type").GetString() == "robot");
    }

    [Fact]
    public async Task PortalLoopMemberAdd_UsesSameLoop_AfterLocalKbRobotIdPromotion()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        await AuthorizeAsync(client, factory);

        var seeded = store.AddLoop(
            "Ghost Loop",
            store.GetAccount().AccountId,
            "Ghost-Instance-Onion-Silk",
            "BOJW-1000-0017-0820-0020");
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "5a0b6398faa0f0001c5d0df1",
            FriendlyName = "Ghost-Instance-Onion-Silk"
        });

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "Intro", lastName = "Person", gender = "female" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        Assert.Contains(
            store.GetLoopMembers(seeded.LoopId),
            member => string.Equals(member.FirstName, "Intro", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            "5a0b6398faa0f0001c5d0df1",
            store.GetLoops().Single(loop => loop.LoopId == seeded.LoopId).RobotId);
    }

    [Fact]
    public async Task PortalAndList_ShareLoop_WhenConfiguredRobotIdSetWithoutPriorUpdateRobot()
    {
        const string configuredHex = "5a0b6398faa0f0001c5d0df1";
        await using var factory = CreateFactory(configuredRobotId: configuredHex);
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        await AuthorizeAsync(client, factory);

        // Friendly-keyed household only — no UpdateRobot yet. Portal must promote + reuse.
        var seeded = store.AddLoop(
            "Ghost Loop",
            store.GetAccount().AccountId,
            "Ghost-Instance-Onion-Silk",
            "BOJW-1000-0017-0820-0020");

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "Shared", lastName = "Roster", gender = "male" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        Assert.Contains(
            store.GetLoopMembers(seeded.LoopId),
            member => string.Equals(member.FirstName, "Shared", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(configuredHex, store.GetLoops().Single(loop => loop.LoopId == seeded.LoopId).RobotId);

        var protocol = new JiboCloudProtocolService(
            store,
            authHandler: new CloudAuthProtocolHandler(store),
            configuration: new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpenJibo:Robot:RobotId"] = configuredHex
                })
                .Build());
        var list = await protocol.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Loop_20160324",
            Operation = "List",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });
        Assert.Equal(200, list.StatusCode);
        using var payload = JsonDocument.Parse(list.BodyText);
        var loops = payload.RootElement.EnumerateArray().ToArray();
        Assert.Single(loops);
        Assert.Equal(seeded.LoopId, loops[0].GetProperty("id").GetString());
        Assert.Equal(configuredHex, loops[0].GetProperty("robot").GetString());
        Assert.Contains(
            loops[0].GetProperty("members").EnumerateArray(),
            member => member.GetProperty("account").GetProperty("firstName").GetString() == "Shared");
    }

    [Fact]
    public async Task PortalAddedMember_SyncsToRobot_AfterFirstUseSigV4CredentialBinding()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        await AuthorizeAsync(client, factory);

        var household = store.AddLoop(
            "Ghost Loop",
            store.GetAccount().AccountId,
            "Ghost-Instance-Onion-Silk",
            "BOJW-1000-0017-0820-0020");

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "FirstUse", lastName = "Bound", gender = "female" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var protocol = factory.Services.GetRequiredService<JiboCloudProtocolService>();
        var bindingsBefore = store.GetRobotCredentialBindings().Count;

        // SSM's Loop#list(): SigV4-signed, no X-Jibo-RobotId header, no bearer token. The
        // household loop here is the only non-bootstrap loop, so this credential must self-bind.
        var list = await protocol.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Loop_20160324",
            Operation = "List",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] =
                    "AWS4-HMAC-SHA256 Credential=AKIAGHOSTFIRSTUSE/20240101/us-east-1/execute-api/aws4_request, " +
                    "SignedHeaders=host;x-amz-date, Signature=deadbeef"
            }
        });

        Assert.Equal(200, list.StatusCode);
        using var payload = JsonDocument.Parse(list.BodyText);
        var loops = payload.RootElement.EnumerateArray().ToArray();
        Assert.Single(loops);
        Assert.Equal(household.LoopId, loops[0].GetProperty("id").GetString());
        Assert.Contains(
            loops[0].GetProperty("members").EnumerateArray(),
            member => member.GetProperty("account").GetProperty("firstName").GetString() == "FirstUse" &&
                      member.GetProperty("status").GetString() == "accepted");
        Assert.Equal(bindingsBefore + 1, store.GetRobotCredentialBindings().Count);
    }

    [Fact]
    public async Task LoopSyncStatus_ReportsNoRobotListCallsWarning_ThenReflectsRobotCall()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        await AuthorizeAsync(client, factory);

        var household = store.AddLoop(
            "Ghost Loop",
            store.GetAccount().AccountId,
            "Ghost-Instance-Onion-Silk",
            "BOJW-1000-0017-0820-0020");

        var beforeResponse = await client.GetAsync("/api/portal/loop-sync-status");
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        var before = await beforeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, before.GetProperty("robotListCallsSeen").GetInt64());
        Assert.Contains(
            before.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString() == "no-robot-list-calls-seen");

        var protocol = factory.Services.GetRequiredService<JiboCloudProtocolService>();
        var list = await protocol.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Loop_20160324",
            Operation = "List",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });
        Assert.Equal(200, list.StatusCode);

        var afterResponse = await client.GetAsync("/api/portal/loop-sync-status");
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        var after = await afterResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, after.GetProperty("robotListCallsSeen").GetInt64());
        Assert.DoesNotContain(
            after.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString() == "no-robot-list-calls-seen");
        Assert.Equal(
            household.LoopId,
            after.GetProperty("lastListLoops").GetProperty("loopId").GetString());
    }

    [Fact]
    public async Task PortalLoopMemberAdd_PushesLoopUpdated_WhenRegisteredUnderFriendlyIdFromToken()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthorizeAsync(client, factory);

        var registry = factory.Services.GetRequiredService<RobotNotificationRegistry>();
        using var socket = new CapturingWebSocket();
        // Mimic ResolveApiSocketRobotKeys after parsing token-FriendlyId-suffix.
        registry.Register(
            ["token-Ghost-Instance-Onion-Silk-abc", "Ghost-Instance-Onion-Silk"],
            socket);

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "Lan", lastName = "Sync", gender = "male" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        Assert.NotNull(socket.LastPayload);
        Assert.Equal(
            "LoopUpdated",
            socket.LastPayload!.Value.GetProperty("payload").GetProperty("name").GetString());
    }

    [Fact]
    public async Task PortalLoopMemberAdd_PushesLoopUpdated_WhenSocketKeyedByDeviceSerial()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthorizeAsync(client, factory);

        var registry = factory.Services.GetRequiredService<RobotNotificationRegistry>();
        using var socket = new CapturingWebSocket();
        // NewRobotToken often keys the socket on serial while Portal session has FriendlyId.
        registry.Register(["BOJW-1000-0017-0820-0020"], socket);

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "Serial", lastName = "Keyed", gender = "female" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        Assert.NotNull(socket.LastPayload);
        Assert.Equal(
            "LoopUpdated",
            socket.LastPayload!.Value.GetProperty("payload").GetProperty("name").GetString());
    }


    [Fact]
    public async Task PortalNameEdit_IsProtectedFromStaleRobotRosterSync()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        await AuthorizeAsync(client, factory);

        var loop = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        store.SyncPeopleFromLoopUsers(
            loop.LoopId,
            "Ghost-Instance-Onion-Silk",
            [new LoopUserSnapshot("looper-zane", "Zane", "Tester", Type: "owner")]);

        var member = store.GetLoopMembers(loop.LoopId)
            .First(item => item.Id.Equals("looper-zane", StringComparison.OrdinalIgnoreCase));

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/portal/loop-members/{member.Id}",
            new { firstName = "Alexander", lastName = "Tester", gender = "male" });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Robot still reports the old name on the next turn.
        store.SyncPeopleFromLoopUsers(
            loop.LoopId,
            "Ghost-Instance-Onion-Silk",
            [new LoopUserSnapshot("looper-zane", "Zane", "Tester", Type: "owner")]);

        var protectedMember = store.GetLoopMembers(loop.LoopId)
            .First(item => item.Id.Equals("looper-zane", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Alexander", protectedMember.FirstName);
        Assert.Equal("male", protectedMember.Gender);
        Assert.NotNull(protectedMember.PortalEditedUtc);

        // Once the robot catches up, clear the portal-edit lock.
        store.SyncPeopleFromLoopUsers(
            loop.LoopId,
            "Ghost-Instance-Onion-Silk",
            [new LoopUserSnapshot("looper-zane", "Alexander", "Tester", Type: "owner")]);

        var caughtUp = store.GetLoopMembers(loop.LoopId)
            .First(item => item.Id.Equals("looper-zane", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Alexander", caughtUp.FirstName);
        Assert.Null(caughtUp.PortalEditedUtc);
    }

    [Fact]
    public async Task PortalAddedMember_IsNotDroppedFromPeopleWhileAwaitingRobotSync()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        await AuthorizeAsync(client, factory);

        var loop = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        store.SyncPeopleFromLoopUsers(
            loop.LoopId,
            "Ghost-Instance-Onion-Silk",
            [new LoopUserSnapshot("looper-zane", "Zane", "Tester", Type: "owner")]);

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "New", lastName = "Friend", gender = "unknown" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var added = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var newMemberId = added.GetProperty("id").GetString()!;

        // Mirror the portal member into people (calendar/identity graph path), then ensure
        // a stale robot roster does not wipe it while PortalEditedUtc is set.
        store.UpsertPerson(new PersonRecord
        {
            PersonId = newMemberId,
            LoopId = loop.LoopId,
            RobotId = "Ghost-Instance-Onion-Silk",
            DisplayName = "New Friend",
            Alias = "New"
        });

        store.SyncPeopleFromLoopUsers(
            loop.LoopId,
            "Ghost-Instance-Onion-Silk",
            [new LoopUserSnapshot("looper-zane", "Zane", "Tester", Type: "owner")]);

        Assert.Contains(
            store.GetPeople(),
            person => person.PersonId.Equals(newMemberId, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            store.GetLoopMembers(loop.LoopId),
            member => member.Id.Equals(newMemberId, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task AuthorizeAsync(
        HttpClient client,
        WebApplicationFactory<Program> factory,
        string friendlyId = "Ghost-Instance-Onion-Silk",
        string deviceId = "BOJW-1000-0017-0820-0020")
    {
        var verificationService = factory.Services.GetRequiredService<JiboVerificationService>();
        var spokenCode = verificationService.IssueCodeForDevice(friendlyId, deviceId);
        var confirmPayload = await (await client.PostAsJsonAsync(
            "/api/portal/jibo-verification/confirm",
            new { code = spokenCode })).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", confirmPayload.GetProperty("portalSessionToken").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(string? configuredRobotId = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-portal-loop-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("OpenJibo:Telemetry:DirectoryPath", Path.Combine(root, "websocket"));
                builder.UseSetting("OpenJibo:ProtocolTelemetry:DirectoryPath", Path.Combine(root, "http"));
                builder.UseSetting("OpenJibo:TurnTelemetry:DirectoryPath", Path.Combine(root, "turn"));
                builder.UseSetting("OpenJibo:Logging:DirectoryPath", Path.Combine(root, "logs"));
                builder.UseSetting(
                    "OpenJibo:UserIntegrations:PersistencePath",
                    Path.Combine(root, "user-integrations.json"));
                builder.UseSetting("OpenJibo:State:Backend", "File");
                builder.UseSetting("OpenJibo:PersonalMemory:Backend", "File");
                builder.UseSetting("OpenJibo:State:PersistencePath", Path.Combine(root, "cloud-state.json"));
                builder.UseSetting(
                    "OpenJibo:PersonalMemory:PersistencePath",
                    Path.Combine(root, "personal-memory.json"));
                builder.UseSetting("OpenJibo:Stt:EnableLocalWhisperCpp", "false");
                if (!string.IsNullOrWhiteSpace(configuredRobotId))
                    builder.UseSetting("OpenJibo:Robot:RobotId", configuredRobotId);
            });
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
