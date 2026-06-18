using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jibo.Cloud.Tests.Api;

public sealed class RobotLaunchRuleApiTests
{
    [Fact]
    public async Task PublicSite_ServesLaunchRulesPage()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/launch-rules.html");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Launch rules for your robot", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_List_Get_And_Delete_LaunchRulesForRobot()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        const string robotName = "Royal-Current-Sage-Canvas";
        const string content = "TopRule = ($* open gallery {%skill='@be/gallery'%} $*);";

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StringContent(content), "files", "gallery.launch.rule");

        var uploadResponse = await client.PostAsync($"/api/public/robots/{robotName}/launch-rules", uploadContent);
        var uploadPayload = await uploadResponse.Content.ReadFromJsonAsync<UploadResponse>();

        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.NotNull(uploadPayload);
        Assert.Equal(robotName, uploadPayload.RobotFriendlyName);
        Assert.Single(uploadPayload.Uploaded);

        var listResponse = await client.GetFromJsonAsync<ListResponse>(
            $"/api/public/robots/{robotName}/launch-rules");
        Assert.NotNull(listResponse);
        Assert.Single(listResponse.Rules);
        Assert.Equal("gallery.launch.rule", listResponse.Rules[0].FileName);

        var getResponse = await client.GetFromJsonAsync<GetResponse>(
            $"/api/public/robots/{robotName}/launch-rules/gallery.launch.rule");
        Assert.NotNull(getResponse);
        Assert.Equal(content, getResponse.Content);

        var deleteResponse = await client.DeleteAsync(
            $"/api/public/robots/{robotName}/launch-rules/gallery.launch.rule");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsInvalidRobotName()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StringContent("TopRule = ($* hi $*);"), "files", "launch.rule");

        var response = await client.PostAsync("/api/public/robots/bad name/launch-rules", uploadContent);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.Error));
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
            });
    }

    private sealed record UploadResponse(string RobotFriendlyName, UploadItem[] Uploaded);

    private sealed record UploadItem(string FileName, long SizeBytes, DateTimeOffset UploadedUtc);

    private sealed record ListResponse(string RobotFriendlyName, RuleSummary[] Rules);

    private sealed record RuleSummary(string FileName, long SizeBytes, DateTimeOffset UploadedUtc);

    private sealed record GetResponse(string RobotFriendlyName, string FileName, string Content);

    private sealed record ErrorResponse(string Error);
}
