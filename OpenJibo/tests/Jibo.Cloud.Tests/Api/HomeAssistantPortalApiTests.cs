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
using Microsoft.Extensions.DependencyInjection;

namespace Jibo.Cloud.Tests.Api;

public sealed class HomeAssistantPortalApiTests
{
    [Fact]
    public async Task LinkFlow_ConnectsHomeAssistantAndLinksJibo()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
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
        var spokenCode =
            verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");

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

        await TryCloseAsync(haSocket);
    }

    [Fact]
    public async Task Unlink_RemovesHomeAssistantLink()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var integrationStore = factory.Services.GetRequiredService<IUserIntegrationStore>();
        integrationStore.AddHomeAssistantLink(
            "BOJW-1000-0017-0820-0020",
            "Ghost-Instance-Onion-Silk",
            "ha-instance-1");

        var verificationService = factory.Services.GetRequiredService<JiboVerificationService>();
        var spokenCode =
            verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
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
        var integrationStore = factory.Services.GetRequiredService<IUserIntegrationStore>();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
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
        var spokenCode =
            verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");

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

        await TryCloseAsync(haSocket);

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
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
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
        var spokenCode =
            verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        var confirmPayload = await (await client.PostAsJsonAsync(
            "/api/portal/jibo-verification/confirm",
            new { code = spokenCode })).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", confirmPayload.GetProperty("portalSessionToken").GetString());

        await client.PostAsJsonAsync(
            "/api/portal/home-assistant/link",
            new { haCode });

        await ReadJsonFrameAsync(haSocket);
        await TryCloseAsync(haSocket);

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


    [Fact]
    public async Task IdentityGraphEndpoint_ReturnsSignedEvidencePayloadForPortalSession()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var verificationService = factory.Services.GetRequiredService<JiboVerificationService>();
        var spokenCode =
            verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");

        var confirmPayload = await (await client.PostAsJsonAsync(
            "/api/portal/jibo-verification/confirm",
            new { code = spokenCode })).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", confirmPayload.GetProperty("portalSessionToken").GetString());

        var graphResponse = await client.GetAsync("/api/portal/identity-graph");

        Assert.Equal(HttpStatusCode.OK, graphResponse.StatusCode);
        var graph = await graphResponse.Content.ReadFromJsonAsync<JsonElement>();
        var contentHash = graph.GetProperty("contentHash").GetString();
        Assert.Matches("^[a-f0-9]{64}$", contentHash);
        Assert.Equal("HMAC-SHA256", graph.GetProperty("signatureAlgorithm").GetString());
        Assert.Equal("open-jibo-local-snapshot-v1", graph.GetProperty("signatureKeyId").GetString());
        Assert.Equal(
            $"1|{graph.GetProperty("accountId").GetString()}|{graph.GetProperty("loopId").GetString()}|{contentHash}",
            graph.GetProperty("signaturePayload").GetString());
        Assert.Matches("^[a-f0-9]{64}$", graph.GetProperty("signature").GetString());
        var admissionAssessment = graph.GetProperty("admissionAssessment");
        Assert.Equal("deny-by-evidence-v1", admissionAssessment.GetProperty("policyVersion").GetString());
        Assert.Equal("quarantine", admissionAssessment.GetProperty("recommendation").GetString());
        var evidenceBundle = graph.GetProperty("evidenceBundle");
        Assert.Equal("identity-graph-evidence-bundle-v1", evidenceBundle.GetProperty("bundleVersion").GetString());
        Assert.Equal(contentHash, evidenceBundle.GetProperty("snapshotContentHash").GetString());
        Assert.Equal(admissionAssessment.GetProperty("decisionHash").GetString(),
            evidenceBundle.GetProperty("admissionDecisionHash").GetString());
        Assert.Equal("quarantine", evidenceBundle.GetProperty("admissionRecommendation").GetString());
        Assert.Contains($"snapshot-content-hash|{contentHash}", evidenceBundle.GetProperty("payload").GetString());
        Assert.Matches("^[a-f0-9]{64}$", evidenceBundle.GetProperty("bundleHash").GetString());
        Assert.Matches("^[a-f0-9]{64}$", evidenceBundle.GetProperty("signature").GetString());
        Assert.Contains("envelope-version|identity-graph-evidence-envelope-v1",
            evidenceBundle.GetProperty("envelope").GetString());
        Assert.Contains($"bundle-signature|{evidenceBundle.GetProperty("signature").GetString()}",
            evidenceBundle.GetProperty("envelope").GetString());
        Assert.Contains(admissionAssessment.GetProperty("satisfiedEvidence").EnumerateArray(), item =>
            item.GetString() == "device-id");
        Assert.Contains(admissionAssessment.GetProperty("blockingEvidence").EnumerateArray(), item =>
            item.GetString() == "required:application-version");
        Assert.Contains(admissionAssessment.GetProperty("recommendedActions").EnumerateArray(), item =>
            item.GetString() == "capture-current-open-jibo-application-version");
        Assert.NotEmpty(graph.GetProperty("relationships").EnumerateArray());
        Assert.Contains(graph.GetProperty("evidenceSignals").EnumerateArray(), signal =>
            signal.GetProperty("signalKind").GetString() == "device-id" &&
            signal.GetProperty("value").GetString() == "BOJW-1000-0017-0820-0020");
    }

    [Fact]
    public async Task IdentityGraphRevocationEndpoint_QuarantinesSignedPeerAdmissionPayload()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var verificationService = factory.Services.GetRequiredService<JiboVerificationService>();
        var spokenCode =
            verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");

        var confirmPayload = await (await client.PostAsJsonAsync(
            "/api/portal/jibo-verification/confirm",
            new { code = spokenCode })).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", confirmPayload.GetProperty("portalSessionToken").GetString());

        var revokeResponse = await client.PostAsJsonAsync(
            "/api/portal/identity-graph/revocations",
            new { anchor = "device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020" });

        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);
        var revoked = await revokeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(revoked.GetProperty("revoked").GetBoolean());
        Assert.Equal("device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020",
            revoked.GetProperty("anchor").GetString());
        var admissionAssessment = revoked.GetProperty("admissionAssessment");
        Assert.Equal("quarantine", admissionAssessment.GetProperty("recommendation").GetString());
        Assert.Contains(admissionAssessment.GetProperty("revocationChecks").EnumerateArray(), item =>
            item.GetString() == "local-revocation-match:device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020");
        Assert.Contains(admissionAssessment.GetProperty("blockingEvidence").EnumerateArray(), item =>
            item.GetString() == "revoked:device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020");
        Assert.Contains(admissionAssessment.GetProperty("recommendedActions").EnumerateArray(), item =>
            item.GetString() == "keep-revoked-identity-anchor-quarantined");
        var evidenceBundle = revoked.GetProperty("evidenceBundle");
        Assert.Equal(admissionAssessment.GetProperty("decisionHash").GetString(),
            evidenceBundle.GetProperty("admissionDecisionHash").GetString());
        Assert.Contains(
            "admission-revocation-checks|local-revocation-match:device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020",
            evidenceBundle.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task IdentityGraphEvidenceBundleEndpoint_ReturnsDownloadablePeerAdmissionPayload()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var verificationService = factory.Services.GetRequiredService<JiboVerificationService>();
        var spokenCode =
            verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");

        var confirmPayload = await (await client.PostAsJsonAsync(
            "/api/portal/jibo-verification/confirm",
            new { code = spokenCode })).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", confirmPayload.GetProperty("portalSessionToken").GetString());

        var bundleResponse = await client.GetAsync("/api/portal/identity-graph/evidence-bundle");

        Assert.Equal(HttpStatusCode.OK, bundleResponse.StatusCode);
        Assert.Equal("text/plain", bundleResponse.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("openjibo-identity-evidence-BOJW-1000-0017-0820-0020-",
            bundleResponse.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var payload = await bundleResponse.Content.ReadAsStringAsync();
        Assert.Contains("envelope-version|identity-graph-evidence-envelope-v1", payload);
        Assert.Contains("bundle-signature|", payload);
        Assert.Contains("payload-begin", payload);
        Assert.Contains("bundle-version|identity-graph-evidence-bundle-v1", payload);
        Assert.Contains("device|BOJW-1000-0017-0820-0020", payload);
        Assert.Contains("admission-recommendation|quarantine", payload);
        Assert.Contains("admission-decision-hash|", payload);
        Assert.Contains("payload-end", payload);
    }


    [Fact]
    public async Task IdentityGraphEvidenceBundleVerifyEndpoint_ReturnsOfflineAdmissionReview()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var verificationService = factory.Services.GetRequiredService<JiboVerificationService>();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            ApplicationVersion = "1.0.20",
            HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["neo-hub.jibo.com"] = "openjibo.local"
            }
        });
        var spokenCode =
            verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");

        var confirmPayload = await (await client.PostAsJsonAsync(
            "/api/portal/jibo-verification/confirm",
            new { code = spokenCode })).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", confirmPayload.GetProperty("portalSessionToken").GetString());
        var graph = store.GetIdentityGraph();

        var verifyResponse = await client.PostAsJsonAsync(
            "/api/portal/identity-graph/evidence-bundle/verify",
            new
            {
                envelope = graph.EvidenceBundle.Envelope,
                localRevokedAnchors = new[]
                {
                    "device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020"
                }
            });

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verification = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verification.GetProperty("isValid").GetBoolean());
        Assert.False(verification.GetProperty("isLocallyAdmissible").GetBoolean());
        Assert.Equal("admit", verification.GetProperty("admissionRecommendation").GetString());
        Assert.Equal("quarantine", verification.GetProperty("effectiveAdmissionRecommendation").GetString());
        Assert.Equal("peer-admission-retention", verification.GetProperty("trustPurpose").GetString());
        Assert.Equal("not-enabled", verification.GetProperty("peerTransportStatus").GetString());
        Assert.Equal("snapshot-retention-only", verification.GetProperty("syncDirection").GetString());
        Assert.False(verification.GetProperty("directPeerTransportAllowed").GetBoolean());
        Assert.Equal("Ghost-Instance-Onion-Silk", verification.GetProperty("robotId").GetString());
        Assert.Contains(verification.GetProperty("localRevocationMatches").EnumerateArray(), item =>
            item.GetString() == "device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020");
        Assert.True(verification.GetProperty("admissionDecisionSignatureValid").GetBoolean());
        Assert.True(verification.GetProperty("snapshotSignatureValid").GetBoolean());
        Assert.Empty(verification.GetProperty("errors").EnumerateArray());
    }

    private static async Task<JsonElement> ReadJsonFrameAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        using var document = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        return document.RootElement.Clone();
    }

    private static async Task TryCloseAsync(WebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived or WebSocketState.CloseSent))
            return;

        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test-complete", CancellationToken.None);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
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