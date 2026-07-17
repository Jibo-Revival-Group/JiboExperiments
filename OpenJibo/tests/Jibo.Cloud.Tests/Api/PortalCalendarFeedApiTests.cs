using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Jibo.Cloud.Tests.Api;

public sealed class PortalCalendarFeedApiTests
{
    [Fact]
    public async Task CalendarFeeds_SaveListAndClear_NeverEchoFullUrl()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        var loopId = store.GetLoops().First().LoopId;
        var member = store.AddLoopMember(
            loopId,
            null,
            "zane@example.test",
            "Zane",
            "Tester",
            "unknown",
            null,
            false,
            "member");

        await AuthorizeAsync(client, factory);

        const string secretUrl = "https://calendar.example.com/ical/zane/private-token/basic.ics";
        var putResponse = await client.PutAsJsonAsync(
            $"/api/portal/calendar-feeds/{member.Id}",
            new { icalUrl = secretUrl, isEnabled = true });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var putBody = await putResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-token", putBody, StringComparison.Ordinal);
        var putPayload = JsonDocument.Parse(putBody).RootElement;
        Assert.True(putPayload.GetProperty("configured").GetBoolean());
        Assert.Equal("calendar.example.com", putPayload.GetProperty("host").GetString());

        var listResponse = await client.GetAsync("/api/portal/calendar-feeds");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-token", listBody, StringComparison.Ordinal);
        Assert.DoesNotContain(secretUrl, listBody, StringComparison.Ordinal);
        Assert.Contains("calendar.example.com", listBody, StringComparison.Ordinal);

        var dashboardResponse = await client.GetAsync("/api/portal/dashboard");
        var dashboardBody = await dashboardResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-token", dashboardBody, StringComparison.Ordinal);

        var deleteResponse = await client.DeleteAsync($"/api/portal/calendar-feeds/{member.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var integrationStore = factory.Services.GetRequiredService<IUserIntegrationStore>();
        Assert.Null(integrationStore.FindMemberCalendarFeed(loopId, member.Id));
    }

    [Fact]
    public async Task CalendarFeeds_RequiresPortalSession()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/portal/calendar-feeds");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CalendarFeeds_RejectsNonHttpsUrl()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        var loopId = store.GetLoops().First().LoopId;
        var member = store.AddLoopMember(
            loopId,
            null,
            "jon@example.test",
            "Jon",
            "Tester",
            "unknown",
            null,
            false,
            "member");

        await AuthorizeAsync(client, factory);

        var response = await client.PutAsJsonAsync(
            $"/api/portal/calendar-feeds/{member.Id}",
            new { icalUrl = "http://calendar.example.com/basic.ics" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task AuthorizeAsync(HttpClient client, WebApplicationFactory<Program> factory)
    {
        var verificationService = factory.Services.GetRequiredService<JiboVerificationService>();
        var spokenCode =
            verificationService.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        var confirmPayload = await (await client.PostAsJsonAsync(
            "/api/portal/jibo-verification/confirm",
            new { code = spokenCode })).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", confirmPayload.GetProperty("portalSessionToken").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-portal-cal-{Guid.NewGuid():N}");
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
            });
    }
}
