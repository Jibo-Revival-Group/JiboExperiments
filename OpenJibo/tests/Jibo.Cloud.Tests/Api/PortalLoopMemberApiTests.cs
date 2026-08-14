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

        // The first person claims the seeded owner, so add someone before the member
        // this test actually exercises.
        await client.PostAsJsonAsync("/api/portal/loop-members", new { firstName = "Casey" });

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new
            {
                firstName = "Jon",
                lastName = "Tester",
                nickname = "JT",
                gender = "male",
                birthday = "2015-06-01",
                isChild = true
            });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var added = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Jon", added.GetProperty("firstName").GetString());
        Assert.Equal("JT", added.GetProperty("nickname").GetString());
        Assert.Equal("male", added.GetProperty("gender").GetString());
        Assert.Equal("2015-06-01", added.GetProperty("birthday").GetString());
        Assert.True(added.GetProperty("isChild").GetBoolean());
        Assert.True(added.GetProperty("canRemove").GetBoolean());
        var memberId = added.GetProperty("id").GetString();

        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        Assert.Contains(store.GetPeople(), person =>
            person.PersonId == memberId &&
            string.Equals(person.DisplayName, "JT", StringComparison.Ordinal));

        var listBody = await (await client.GetAsync("/api/portal/loop-members")).Content.ReadAsStringAsync();
        Assert.Contains("Jon", listBody, StringComparison.Ordinal);

        var dashboardBody = await (await client.GetAsync("/api/portal/dashboard")).Content.ReadAsStringAsync();
        Assert.Contains("Jon", dashboardBody, StringComparison.Ordinal);
        Assert.Contains(memberId!, dashboardBody, StringComparison.Ordinal);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/portal/loop-members/{memberId}",
            new
            {
                firstName = "Jonathan",
                lastName = "Tester",
                nickname = "Jonny",
                gender = "female",
                birthday = "2014-01-15",
                isChild = false
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Jonathan", updated.GetProperty("firstName").GetString());
        Assert.Equal("Jonny", updated.GetProperty("nickname").GetString());
        Assert.Equal("female", updated.GetProperty("gender").GetString());
        Assert.Equal("2014-01-15", updated.GetProperty("birthday").GetString());
        Assert.False(updated.GetProperty("isChild").GetBoolean());

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

    /// <summary>
    /// The placeholder "Jibo Owner" has a truthy firstName, so the robot shows it in
    /// introductions as a person who does not exist. It cannot simply be deleted: the
    /// robot's _applyLoopChanges throws if no member carries loop.owner. The first real
    /// person therefore takes the record over in place — same member id, so SyncManager
    /// renames the existing KB UserNode rather than leaving a "Jibo" node behind.
    /// </summary>
    [Fact]
    public async Task LoopMembers_FirstAddClaimsTheSeededOwnerInPlace()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        await AuthorizeAsync(client, factory);

        var before = await (await client.GetAsync("/api/portal/loop-members"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var loopId = before.GetProperty("loopId").GetString()!;
        var seededOwner = before.GetProperty("members").EnumerateArray().Single();
        Assert.Equal("Jibo", seededOwner.GetProperty("firstName").GetString());

        var addResponse = await client.PostAsJsonAsync(
            "/api/portal/loop-members",
            new { firstName = "Zane", lastName = "Ricci" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var added = await addResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(seededOwner.GetProperty("id").GetString(), added.GetProperty("id").GetString());
        Assert.Equal("owner", added.GetProperty("type").GetString());

        var household = store.GetLoopMembers(loopId).Where(m => m.Type != "robot").ToArray();
        var owner = Assert.Single(household);
        Assert.Equal("Zane", owner.FirstName);
        Assert.DoesNotContain(store.GetLoopMembers(loopId), m =>
            string.Equals(m.FirstName, "Jibo", StringComparison.OrdinalIgnoreCase));

        // The robot resolves loop.owner through members[].accountId; losing that mapping
        // is a TypeError mid-sync, not a warning.
        var loop = store.GetLoops().Single(l => l.LoopId == loopId);
        Assert.Equal(loop.OwnerAccountId, owner.AccountId);

        Assert.DoesNotContain(store.GetPeople(), person =>
            string.Equals(person.DisplayName, "Jibo Owner", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoopMembers_MakeOwnerMovesOwnershipAndDemotesThePrevious()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        await AuthorizeAsync(client, factory);

        var first = await (await client.PostAsJsonAsync(
            "/api/portal/loop-members", new { firstName = "Zane" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await client.PostAsJsonAsync(
            "/api/portal/loop-members", new { firstName = "Casey" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("owner", first.GetProperty("type").GetString());
        Assert.Equal("member", second.GetProperty("type").GetString());
        Assert.True(second.GetProperty("canMakeOwner").GetBoolean());

        var secondId = second.GetProperty("id").GetString();
        var promoteResponse = await client.PostAsync(
            $"/api/portal/loop-members/{secondId}/make-owner", null);
        Assert.Equal(HttpStatusCode.OK, promoteResponse.StatusCode);
        var promoted = await promoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("owner", promoted.GetProperty("type").GetString());

        var loop = store.GetLoops().Single(l => l.LoopId == promoted.GetProperty("loopId").GetString());
        var household = store.GetLoopMembers(loop.LoopId).Where(m => m.Type != "robot").ToArray();
        Assert.Equal(2, household.Length);

        var owner = Assert.Single(household, m => m.Type == "owner");
        Assert.Equal("Casey", owner.FirstName);
        Assert.Equal(loop.OwnerAccountId, owner.AccountId);

        // Exactly one member may carry the owner account id, or the robot's
        // memberIdsByAccountId lookup becomes ambiguous.
        Assert.Single(household, m =>
            string.Equals(m.AccountId, loop.OwnerAccountId, StringComparison.OrdinalIgnoreCase));

        var demoted = Assert.Single(household, m => m.FirstName == "Zane");
        Assert.Equal("member", demoted.Type);
    }

    [Fact]
    public async Task LoopMembers_OwnerRemovalHandsOwnershipToSomeoneElse()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        await AuthorizeAsync(client, factory);

        var owner = await (await client.PostAsJsonAsync(
            "/api/portal/loop-members", new { firstName = "Zane" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        await client.PostAsJsonAsync("/api/portal/loop-members", new { firstName = "Casey" });

        var removeResponse = await client.DeleteAsync(
            $"/api/portal/loop-members/{owner.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        var loop = store.GetLoops().Single(l => l.LoopId == owner.GetProperty("loopId").GetString());
        var household = store.GetLoopMembers(loop.LoopId).Where(m => m.Type != "robot").ToArray();
        var survivor = Assert.Single(household);
        Assert.Equal("Casey", survivor.FirstName);
        Assert.Equal("owner", survivor.Type);
        Assert.Equal(loop.OwnerAccountId, survivor.AccountId);
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
