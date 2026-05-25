using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using Jibo.Cloud.Application.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jibo.Cloud.Tests.Api;

public sealed class JiboCloudApiIntegrationTests
{
    [Fact]
    public async Task Health_ReturnsCurrentVersion()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.True(body!.Ok);
        Assert.Equal("OpenJibo Cloud Api", body.Service);
        Assert.Equal(OpenJiboCloudBuildInfo.Version, body.Version);
    }

    [Fact]
    public async Task HttpProtocolDispatch_HandlesCreateHubTokenTarget()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.TryAddWithoutValidation("X-Amz-Target", "Account_20160715.CreateHubToken");
        request.Headers.Host = "api.jibo.com";

        var response = await client.SendAsync(request);
        var payload = await response.Content.ReadFromJsonAsync<CreateHubTokenResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.Token));
    }

    [Fact]
    public async Task WebSocket_MissingTokenOnNeoHubListen_ReturnsUnauthorized()
    {
        await using var factory = CreateFactory();
        var client = factory.Server.CreateWebSocketClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ConnectAsync(new Uri("ws://neo-hub.jibo.com/"), CancellationToken.None));

        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebSocket_TokenPathOnNeoHubListen_Connects()
    {
        await using var factory = CreateFactory();
        var client = factory.Server.CreateWebSocketClient();

        using var socket = await client.ConnectAsync(new Uri("ws://neo-hub.jibo.com/test-token"), CancellationToken.None);

        Assert.Equal(WebSocketState.Open, socket.State);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test-complete", CancellationToken.None);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-api-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("OpenJibo:Telemetry:DirectoryPath", Path.Combine(root, "websocket"));
                builder.UseSetting("OpenJibo:ProtocolTelemetry:DirectoryPath", Path.Combine(root, "http"));
                builder.UseSetting("OpenJibo:TurnTelemetry:DirectoryPath", Path.Combine(root, "turn"));
                builder.UseSetting("OpenJibo:State:PersistencePath", Path.Combine(root, "cloud-state.json"));
                builder.UseSetting("OpenJibo:PersonalMemory:PersistencePath", Path.Combine(root, "personal-memory.json"));
                builder.UseSetting("OpenJibo:Media:DirectoryPath", Path.Combine(root, "media"));
            });
    }

    private sealed record HealthResponse(bool Ok, string Service, string Version);

    private sealed record CreateHubTokenResponse(string Token);
}
