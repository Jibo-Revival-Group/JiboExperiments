using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jibo.Cloud.Tests.Api;

public sealed class LaunchRuleAdminApiTests
{
    private const string AdminPassword = "test-launch-rules-password";

    [Fact]
    public async Task LaunchRulesPage_RequiresAdminPassword()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/launch-rules.html");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LaunchRulesPage_ServesWithAdminPassword()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var response = await client.GetAsync("/launch-rules.html");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Global launch rules", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_List_Get_And_Delete_GlobalLaunchRules()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);
        const string content = "TopRule = ($* open gallery {%skill='@be/gallery'%} $*);";

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StringContent(content), "files", "gallery.launch.rule");

        var uploadResponse = await client.PostAsync("/api/admin/launch-rules", uploadContent);
        var uploadPayload = await uploadResponse.Content.ReadFromJsonAsync<UploadResponse>();

        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.NotNull(uploadPayload);
        Assert.Equal("global", uploadPayload.Scope);
        Assert.Single(uploadPayload.Uploaded);

        var listResponse = await client.GetFromJsonAsync<ListResponse>("/api/admin/launch-rules");
        Assert.NotNull(listResponse);
        Assert.Equal("global", listResponse.Scope);
        Assert.Single(listResponse.Rules);
        Assert.Equal("gallery.launch.rule", listResponse.Rules[0].FileName);

        var getResponse = await client.GetFromJsonAsync<GetResponse>(
            "/api/admin/launch-rules/gallery.launch.rule");
        Assert.NotNull(getResponse);
        Assert.Equal(content, getResponse.Content);

        var deleteResponse = await client.DeleteAsync("/api/admin/launch-rules/gallery.launch.rule");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsInvalidFileExtension()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StringContent("TopRule = ($* hi $*);"), "files", "launch.txt");

        var response = await client.PostAsync("/api/admin/launch-rules", uploadContent);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.Error));
    }

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"admin:{AdminPassword}")));
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-launch-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("OpenJibo:Telemetry:DirectoryPath", Path.Combine(root, "websocket"));
                builder.UseSetting("OpenJibo:ProtocolTelemetry:DirectoryPath", Path.Combine(root, "http"));
                builder.UseSetting("OpenJibo:TurnTelemetry:DirectoryPath", Path.Combine(root, "turn"));
                builder.UseSetting("OpenJibo:State:PersistencePath", Path.Combine(root, "cloud-state.json"));
                builder.UseSetting("OpenJibo:PersonalMemory:PersistencePath",
                    Path.Combine(root, "personal-memory.json"));
                builder.UseSetting("OpenJibo:Media:DirectoryPath", Path.Combine(root, "media"));
                builder.UseSetting("OpenJibo:LaunchRules:DirectoryPath", Path.Combine(root, "launch-rules"));
                builder.UseSetting("OPENJIBO_LAUNCH_RULES_PASSWORD", AdminPassword);
            });
    }

    private sealed record UploadResponse(string Scope, UploadItem[] Uploaded);

    private sealed record UploadItem(string FileName, long SizeBytes, DateTimeOffset UploadedUtc);

    private sealed record ListResponse(string Scope, RuleSummary[] Rules);

    private sealed record RuleSummary(string FileName, long SizeBytes, DateTimeOffset UploadedUtc);

    private sealed record GetResponse(string Scope, string FileName, string Content);

    private sealed record ErrorResponse(string Error);
}
