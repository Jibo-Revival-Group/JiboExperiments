using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Jibo.Cloud.Tests.Api;

public sealed class PortalPhotosApiTests
{
    [Fact]
    public async Task Photos_ListAndContent_ServeLoopImagesOnly()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthorizeAsync(client, factory);

        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        var mediaStore = factory.Services.GetRequiredService<IMediaContentStore>();
        var loopId = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "Ghost-Instance-Onion-Silk").LoopId;

        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x00 };
        await mediaStore.StoreAsync("/media/photo-a.jpg", "image/jpeg", jpeg,
            new Dictionary<string, object?> { ["contentType"] = "image/jpeg" });
        store.CreateMedia(loopId, "/media/photo-a.jpg", "image", "robot", false,
            new Dictionary<string, object?> { ["contentType"] = "image/jpeg" });

        store.CreateMedia(loopId, "/media/notes.txt", "text", "robot", false,
            new Dictionary<string, object?> { ["contentType"] = "text/plain" });
        store.CreateMedia(loopId, "/media/deleted.jpg", "image", "robot", false,
            new Dictionary<string, object?> { ["contentType"] = "image/jpeg" });
        store.RemoveMedia(["/media/deleted.jpg"]);

        var otherLoop = store.AddLoop(null, null, "Other-Robot", "Other-Robot").LoopId;
        await mediaStore.StoreAsync("/media/other.jpg", "image/jpeg", jpeg,
            new Dictionary<string, object?> { ["contentType"] = "image/jpeg" });
        store.CreateMedia(otherLoop, "/media/other.jpg", "image", "robot", false,
            new Dictionary<string, object?> { ["contentType"] = "image/jpeg" });

        var listResponse = await client.GetAsync("/api/portal/photos");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, list.GetProperty("count").GetInt32());
        Assert.Equal("/media/photo-a.jpg", list.GetProperty("photos")[0].GetProperty("path").GetString());

        var contentResponse = await client.GetAsync("/api/portal/photos/content?path=%2Fmedia%2Fphoto-a.jpg");
        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
        Assert.Equal("image/jpeg", contentResponse.Content.Headers.ContentType?.MediaType);
        var bytes = await contentResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(jpeg, bytes);

        var missing = await client.GetAsync("/api/portal/photos/content?path=%2Fmedia%2Fother.jpg");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var deleted = await client.GetAsync("/api/portal/photos/content?path=%2Fmedia%2Fdeleted.jpg");
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    [Fact]
    public async Task Photos_RequirePortalSession()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/portal/photos");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-portal-photos-{Guid.NewGuid():N}");
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
                builder.UseSetting("OpenJibo:Media:Backend", "File");
                builder.UseSetting("OpenJibo:Media:DirectoryPath", Path.Combine(root, "media"));
                builder.UseSetting("OpenJibo:State:PersistencePath", Path.Combine(root, "cloud-state.json"));
                builder.UseSetting(
                    "OpenJibo:PersonalMemory:PersistencePath",
                    Path.Combine(root, "personal-memory.json"));
                builder.UseSetting("OpenJibo:Stt:EnableLocalWhisperCpp", "false");
            });
    }
}
