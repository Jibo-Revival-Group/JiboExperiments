using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Tests.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Jibo.Cloud.Tests.Api;

public sealed class PortalLoopMemberApiTests
{
    [Fact]
    public async Task LoopProfilePhoto_UploadReplaceAndDownloadStayWithinTheVerifiedLoop()
    {
        const string deviceId = "profile-photo-device";
        const string robotId = "profile-photo-robot";
        var credentials = new AccountProfile
        {
            AccessKeyId = "profile-photo-stock-key", SecretAccessKey = "profile-photo-stock-test-secret"
        };
        await using var factory = CreateFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("OpenJibo:CanonicalApiHostname", "api.jibo.com");
            builder.UseSetting("OpenJibo:CanonicalApiBaseUrl", "https://api.jibo.com");
            builder.UseSetting("OpenJibo:Security:StockLoopCredential", JsonSerializer.Serialize(new
            {
                deviceId, region = "open-jibo", credentials.AccessKeyId, credentials.SecretAccessKey
            }));
        });
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        using var robot = factory.CreateClient();
        await AuthorizeAsync(owner, factory, robotId, deviceId);
        await AuthorizeAsync(other, factory, "another-photo-robot", "another-photo-device");

        using var added = await owner.PostAsJsonAsync("/api/portal/loop-members", new { firstName = "Pat" });
        added.EnsureSuccessStatusCode();
        var member = await added.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(member.GetProperty("canUploadPhoto").GetBoolean());
        var memberId = member.GetProperty("id").GetString();
        var photoPath = $"/api/portal/loop-members/{memberId}/photo";
        var original = Convert.FromBase64String(RedJpeg);
        var replacement = Convert.FromBase64String(BlueJpeg);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(photoPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await robot.PutAsync(photoPath, Jpeg(original))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync(photoPath, Jpeg(original))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsync(photoPath, Jpeg([0xff, 0xd8, 0xff, 0xd9]))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await owner.PutAsync(photoPath, Jpeg(new byte[LoopProfilePhotoService.MaxBytes + 1]))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync(photoPath, Jpeg(original))).StatusCode);
        using var preview = await owner.GetAsync(photoPath);
        Assert.Equal("image/jpeg", preview.Content.Headers.ContentType?.MediaType);
        Assert.True(preview.Headers.CacheControl?.NoStore);
        Assert.Equal(original, await preview.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(photoPath)).StatusCode);

        var service = factory.Services.GetRequiredService<JiboCloudProtocolService>();
        async Task<Uri> ReadRobotPhotoUrlAsync()
        {
            var signed = AwsSigV4RequestVerifierTests.Sign(credentials, "{}", DateTimeOffset.UtcNow,
                ["host", "x-amz-content-sha256", "x-amz-date", "x-amz-target"], region: "open-jibo",
                service: "Loop", target: "Loop_20160324.ListLoops");
            var reply = await service.DispatchAsync(new ProtocolEnvelope
            {
                Method = "POST", Path = "/", HostName = "api.jibo.com", ServicePrefix = "Loop_20160324",
                Operation = "ListLoops", Headers = signed.Headers, BodyBytes = signed.BodyBytes, BodyText = signed.BodyText
            });
            Assert.Equal(200, reply.StatusCode);
            using var json = JsonDocument.Parse(reply.BodyText);
            var item = json.RootElement[0].GetProperty("members").EnumerateArray().Single(value =>
                value.GetProperty("id").GetString() == memberId);
            Assert.False(item.GetProperty("enrolled").GetProperty("face").GetBoolean());
            Assert.False(item.GetProperty("enrolled").GetProperty("voice").GetBoolean());
            return new Uri(item.GetProperty("account").GetProperty("photoUrl").GetString()!);
        }

        var originalUrl = await ReadRobotPhotoUrlAsync();
        Assert.Equal("api.jibo.com", originalUrl.Host);
        Assert.Equal(original, await robot.GetByteArrayAsync(originalUrl.PathAndQuery));
        Assert.Equal(HttpStatusCode.NotFound, (await robot.GetAsync(originalUrl.AbsolutePath)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync(photoPath, Jpeg(replacement))).StatusCode);
        var replacedUrl = await ReadRobotPhotoUrlAsync();
        Assert.NotEqual(originalUrl.AbsolutePath, replacedUrl.AbsolutePath);
        Assert.Equal(HttpStatusCode.NotFound, (await robot.GetAsync(originalUrl.PathAndQuery)).StatusCode);
        Assert.Equal(replacement, await robot.GetByteArrayAsync(replacedUrl.PathAndQuery));
        Assert.Equal(replacement, await owner.GetByteArrayAsync(photoPath));

        var mismatchedTicket = originalUrl.AbsolutePath + replacedUrl.Query;
        Assert.Equal(HttpStatusCode.NotFound, (await robot.GetAsync(mismatchedTicket)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/portal/loop-members/{memberId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await robot.GetAsync(replacedUrl.PathAndQuery)).StatusCode);
    }

    private static ByteArrayContent Jpeg(byte[] bytes) => new(bytes)
    {
        Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") }
    };

    private const string RedJpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAAIAAgDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAb/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAABQf/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCMACK2/9k=";

    private const string BlueJpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAAIAAgDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAABgf/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCaAeJ4/9k=";

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
    public async Task RemovedLoopMember_DoesNotReturnAfterOnDeviceRosterReplay()
    {
        const string robotId = "roster-test-robot";
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await AuthorizeAsync(client, factory, robotId, "roster-test-device");
        var store = factory.Services.GetRequiredService<ICloudStateStore>();
        var loop = store.AddLoop(null, null, robotId, robotId);
        var stale = new LoopUserSnapshot("former-member", FirstName: "Former", AccountId: "former-account");
        Assert.Equal(1, store.SyncPeopleFromLoopUsers(loop.LoopId, robotId, [stale]));

        using var removal = await client.DeleteAsync("/api/portal/loop-members/former-member");
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);
        var current = new LoopUserSnapshot("current-member", FirstName: "Current");
        Assert.Equal(1, store.SyncPeopleFromLoopUsers(loop.LoopId, robotId,
            [stale, stale with { Id = "alternate-local-id" }, current]));

        var dashboard = await client.GetStringAsync("/api/portal/dashboard");
        Assert.Contains("current-member", dashboard);
        Assert.DoesNotContain("former-member", dashboard);
        Assert.DoesNotContain("alternate-local-id", dashboard);
        Assert.DoesNotContain(store.GetPeople(loop.LoopId), person => person.PersonId == stale.Id);
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
                builder.UseSetting("OpenJibo:Media:DirectoryPath", Path.Combine(root, "media"));
                builder.UseSetting("OpenJibo:PersonalMemory:Backend", "File");
                builder.UseSetting("OpenJibo:State:PersistencePath", Path.Combine(root, "cloud-state.json"));
                builder.UseSetting(
                    "OpenJibo:PersonalMemory:PersistencePath",
                    Path.Combine(root, "personal-memory.json"));
                builder.UseSetting("OpenJibo:Stt:EnableLocalWhisperCpp", "false");
                builder.UseSetting("OpenJibo:Stt:EnableWhisperServer", "false");
            });
    }
}
