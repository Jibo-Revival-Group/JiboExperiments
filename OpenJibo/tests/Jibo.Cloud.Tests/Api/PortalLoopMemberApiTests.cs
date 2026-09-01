using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Jibo.Cloud.Tests.Api;

public sealed class PortalLoopMemberApiTests
{
    [Fact]
    public async Task LoopMembers_AddUpdateAndRemove_RoundTrips()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthorizeAsync(client, factory);

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "Jon", lastName = "Tester", gender = "male" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var added = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Jon", added.GetProperty("firstName").GetString());
        Assert.Equal("male", added.GetProperty("gender").GetString());
        Assert.True(added.GetProperty("canRemove").GetBoolean());
        var memberId = added.GetProperty("id").GetString();

        var listBody = await (await client.GetAsync("/api/portal/loop-members")).Content.ReadAsStringAsync();
        Assert.Contains("Jon", listBody, StringComparison.Ordinal);

        var dashboardBody = await (await client.GetAsync("/api/portal/dashboard")).Content.ReadAsStringAsync();
        Assert.Contains("Jon", dashboardBody, StringComparison.Ordinal);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/portal/loop-members/{memberId}",
            new { firstName = "Jonathan", lastName = "Tester", gender = "female" });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Jonathan", updated.GetProperty("firstName").GetString());
        Assert.Equal("female", updated.GetProperty("gender").GetString());

        var removeResponse = await client.DeleteAsync($"/api/portal/loop-members/{memberId}");
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        var listAfterRemove = await (await client.GetAsync("/api/portal/loop-members")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Jonathan", listAfterRemove, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoopMembers_NormalizesUnknownGenderValues()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthorizeAsync(client, factory);

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "Ash", gender = "not-a-real-gender" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var added = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unknown", added.GetProperty("gender").GetString());
    }

    [Fact]
    public async Task LoopMembers_AddAssignsAccountIdAndReportsPushDelivery()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthorizeAsync(client, factory);

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "Pat", lastName = "Person", gender = "unknown" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var added = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(added.GetProperty("accountId").GetString()));
        Assert.StartsWith("acct-", added.GetProperty("accountId").GetString());
        Assert.True(added.TryGetProperty("loopUpdatedPushCount", out var pushCount));
        Assert.Equal(0, pushCount.GetInt32());
        Assert.False(added.GetProperty("loopUpdatedDelivered").GetBoolean());

        var dashboard = await (await client.GetAsync("/api/portal/dashboard")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dashboard.TryGetProperty("loopSync", out var loopSync));
        Assert.False(loopSync.GetProperty("apiSocketMatchedForThisRobot").GetBoolean());
        Assert.Equal(0, loopSync.GetProperty("apiSocketOpenConnections").GetInt32());
    }

    [Fact]
    public async Task LoopMembers_CannotRemoveOwnerOrRobot()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        await AuthorizeAsync(client, factory);

        var loop = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        var owner = store.GetLoopMembers(loop.LoopId).First(m => m.Type == "owner");

        var listBody = await (await client.GetAsync("/api/portal/loop-members")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"type\":\"robot\"", listBody, StringComparison.Ordinal);

        var ownerJson = JsonDocument.Parse(listBody).RootElement
            .GetProperty("members")
            .EnumerateArray()
            .First(m => m.GetProperty("id").GetString() == owner.Id);
        Assert.False(ownerJson.GetProperty("canRemove").GetBoolean());

        var removeResponse = await client.DeleteAsync($"/api/portal/loop-members/{owner.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, removeResponse.StatusCode);
    }

    [Fact]
    public async Task LoopMembers_RejectsBlankFirstName()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthorizeAsync(client, factory);

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "   ", gender = "unknown" });
        Assert.Equal(HttpStatusCode.BadRequest, addResponse.StatusCode);

        var secondAddResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "Pat" });
        var added = await secondAddResponse.Content.ReadFromJsonAsync<JsonElement>();
        var memberId = added.GetProperty("id").GetString();

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/portal/loop-members/{memberId}",
            new { firstName = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
    }

    [Fact]
    public async Task LoopMembers_RequiresPortalSession()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var listResponse = await client.GetAsync("/api/portal/loop-members");
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "Nobody" });
        Assert.Equal(HttpStatusCode.Unauthorized, addResponse.StatusCode);
    }

    [Fact]
    public async Task LoopMembers_UpdateReturnsNotFoundForUnknownMember()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthorizeAsync(client, factory);

        var updateResponse = await client.PutAsJsonAsync(
            "/api/portal/loop-members/does-not-exist",
            new { firstName = "Ghost" });
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);

        var removeResponse = await client.DeleteAsync("/api/portal/loop-members/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, removeResponse.StatusCode);
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
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-portal-loop-members-{Guid.NewGuid():N}");
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
