using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Jibo.Cloud.Tests.Api;

public sealed class HomeAssistantPortalApiTests
{
    [Fact]
    public async Task LinkFlow_ConnectsHomeAssistantAndLinksJibo()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<Jibo.Cloud.Application.Abstractions.ICloudStateStore>();
        var robot = store.GetRobot();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = robot.FriendlyName,
            FirmwareVersion = robot.FirmwareVersion,
            ApplicationVersion = robot.ApplicationVersion,
            HostMappings = new Dictionary<string, string>(robot.HostMappings, StringComparer.OrdinalIgnoreCase)
        });

        var wsClient = factory.Server.CreateWebSocketClient();
        using var haSocket = await wsClient.ConnectAsync(
            new Uri("ws://localhost/v1/homeassistant/ws"),
            CancellationToken.None);

        var registerBytes = Encoding.UTF8.GetBytes("""{"type":"register","instanceId":"ha-test-instance"}""");
        await haSocket.SendAsync(registerBytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var codePayload = await ReadJsonFrameAsync(haSocket);
        Assert.Equal("verification_code", codePayload.GetProperty("type").GetString());

        var haCode = codePayload.GetProperty("code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(haCode));

        var verificationService = factory.Services.GetRequiredService<JiboVerificationService>();
        var spokenCode = verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");

        var confirmResponse = await client.PostAsJsonAsync(
            "/api/portal/jibo-verification/confirm",
            new { code = spokenCode });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmPayload = await confirmResponse.Content.ReadFromJsonAsync<JsonElement>();
        var portalSessionToken = confirmPayload.GetProperty("portalSessionToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(portalSessionToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", portalSessionToken);

        var dashboardResponse = await client.GetAsync("/api/portal/dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        var dashboardPayload = await dashboardResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ghost-Instance-Onion-Silk", dashboardPayload.GetProperty("jiboFriendlyId").GetString());
        Assert.False(dashboardPayload.GetProperty("homeAssistant").GetProperty("linked").GetBoolean());

        var linkResponse = await client.PostAsJsonAsync(
            "/api/portal/home-assistant/link",
            new { haCode });
        Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);

        var pairedPayload = await ReadJsonFrameAsync(haSocket);
        Assert.Equal("paired", pairedPayload.GetProperty("type").GetString());

        var linksResponse = await client.GetAsync("/api/portal/home-assistant/links");
        var linksPayload = await linksResponse.Content.ReadFromJsonAsync<JsonElement>();
        var links = linksPayload.GetProperty("links");
        Assert.Equal(1, links.GetArrayLength());
        Assert.True(links[0].GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task Unlink_RemovesHomeAssistantLink()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var integrationStore = factory.Services.GetRequiredService<Jibo.Cloud.Application.Abstractions.IUserIntegrationStore>();
        integrationStore.AddHomeAssistantLink(
            "BOJW-1000-0017-0820-0020",
            "Ghost-Instance-Onion-Silk",
            "ha-instance-1");

        var verificationService = factory.Services.GetRequiredService<JiboVerificationService>();
        var spokenCode = verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        var confirmPayload = await (await client.PostAsJsonAsync(
            "/api/portal/jibo-verification/confirm",
            new { code = spokenCode })).Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", confirmPayload.GetProperty("portalSessionToken").GetString());

        var unlinkResponse = await client.DeleteAsync("/api/portal/home-assistant/link");
        Assert.Equal(HttpStatusCode.OK, unlinkResponse.StatusCode);

        Assert.Null(integrationStore.FindLinkForJibo("BOJW-1000-0017-0820-0020", "Ghost-Instance-Onion-Silk"));
    }

    [Fact]
    public async Task Register_ReconnectsAsPaired_WhenLinkAlreadyExists()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var integrationStore = factory.Services.GetRequiredService<Jibo.Cloud.Application.Abstractions.IUserIntegrationStore>();
        var store = factory.Services.GetRequiredService<Jibo.Cloud.Application.Abstractions.ICloudStateStore>();
        var robot = store.GetRobot();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = robot.FriendlyName,
            FirmwareVersion = robot.FirmwareVersion,
            ApplicationVersion = robot.ApplicationVersion,
            HostMappings = new Dictionary<string, string>(robot.HostMappings, StringComparer.OrdinalIgnoreCase)
        });

        var wsClient = factory.Server.CreateWebSocketClient();
        using var haSocket = await wsClient.ConnectAsync(
            new Uri("ws://localhost/v1/homeassistant/ws"),
            CancellationToken.None);

        await haSocket.SendAsync(
            Encoding.UTF8.GetBytes("""{"type":"register","instanceId":"ha-test-instance"}"""),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

        var codePayload = await ReadJsonFrameAsync(haSocket);
        var haCode = codePayload.GetProperty("code").GetString();

        var verificationService = factory.Services.GetRequiredService<JiboVerificationService>();
        var spokenCode = verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");

        var confirmPayload = await (await client.PostAsJsonAsync(
            "/api/portal/jibo-verification/confirm",
            new { code = spokenCode })).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", confirmPayload.GetProperty("portalSessionToken").GetString());

        var linkResponse = await client.PostAsJsonAsync(
            "/api/portal/home-assistant/link",
            new { haCode });
        var linkPayload = await linkResponse.Content.ReadFromJsonAsync<JsonElement>();
        var linkId = linkPayload.GetProperty("linkId").GetString();

        var pairedPayload = await ReadJsonFrameAsync(haSocket);
        Assert.Equal("paired", pairedPayload.GetProperty("type").GetString());

        await haSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "restart", CancellationToken.None);

        using var reconnectedSocket = await wsClient.ConnectAsync(
            new Uri("ws://localhost/v1/homeassistant/ws"),
            CancellationToken.None);
        await reconnectedSocket.SendAsync(
            Encoding.UTF8.GetBytes(
                $$"""{"type":"register","instanceId":"ha-test-instance","linkId":"{{linkId}}"}"""),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

        var reconnectPayload = await ReadJsonFrameAsync(reconnectedSocket);
        Assert.Equal("paired", reconnectPayload.GetProperty("type").GetString());
        Assert.Equal(linkId, reconnectPayload.GetProperty("linkId").GetString());

        var link = integrationStore.FindLinkByLinkId(linkId!);
        Assert.NotNull(link);
        Assert.True(link.LastSeenUtc > link.PairedAtUtc);
    }

    [Fact]
    public async Task Register_ReconnectsAsPaired_WhenOnlyInstanceIdIsKnown()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<Jibo.Cloud.Application.Abstractions.ICloudStateStore>();
        var robot = store.GetRobot();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = robot.FriendlyName,
            FirmwareVersion = robot.FirmwareVersion,
            ApplicationVersion = robot.ApplicationVersion,
            HostMappings = new Dictionary<string, string>(robot.HostMappings, StringComparer.OrdinalIgnoreCase)
        });

        var wsClient = factory.Server.CreateWebSocketClient();
        using var haSocket = await wsClient.ConnectAsync(
            new Uri("ws://localhost/v1/homeassistant/ws"),
            CancellationToken.None);

        await haSocket.SendAsync(
            Encoding.UTF8.GetBytes("""{"type":"register","instanceId":"ha-known-instance"}"""),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

        var codePayload = await ReadJsonFrameAsync(haSocket);
        var haCode = codePayload.GetProperty("code").GetString();

        var verificationService = factory.Services.GetRequiredService<JiboVerificationService>();
        var spokenCode = verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        var confirmPayload = await (await client.PostAsJsonAsync(
            "/api/portal/jibo-verification/confirm",
            new { code = spokenCode })).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", confirmPayload.GetProperty("portalSessionToken").GetString());

        await client.PostAsJsonAsync(
            "/api/portal/home-assistant/link",
            new { haCode });

        await ReadJsonFrameAsync(haSocket);
        await haSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "restart", CancellationToken.None);

        using var reconnectedSocket = await wsClient.ConnectAsync(
            new Uri("ws://localhost/v1/homeassistant/ws"),
            CancellationToken.None);
        await reconnectedSocket.SendAsync(
            Encoding.UTF8.GetBytes("""{"type":"register","instanceId":"ha-known-instance"}"""),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

        var reconnectPayload = await ReadJsonFrameAsync(reconnectedSocket);
        Assert.Equal("paired", reconnectPayload.GetProperty("type").GetString());
    }

    private static async Task<JsonElement> ReadJsonFrameAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        using var document = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        return document.RootElement.Clone();
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-portal-tests-{Guid.NewGuid():N}");
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
            });
    }
}
