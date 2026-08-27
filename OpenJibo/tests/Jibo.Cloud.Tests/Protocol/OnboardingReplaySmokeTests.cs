using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Protocol;

public sealed class OnboardingReplaySmokeTests
{
    private readonly JiboCloudProtocolService _service = new(new InMemoryCloudStateStore());

    [Fact]
    public async Task OnboardingSequence_ReplaysAccountLoopAndOobeSetup()
    {
        var email = $"openjibo-smoke-{Guid.NewGuid():N}@example.com";
        const string password = "OpenJiboSmokePass!42";
        const string robotId = "onboarding-replay-robot";

        var create = await DispatchAsync("Account_20151111", "Create", new
        {
            email,
            password,
            firstName = "Open",
            lastName = "Jibo"
        });

        Assert.Equal(200, create.StatusCode);
        using var createPayload = JsonDocument.Parse(create.BodyText);
        var accountId = createPayload.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accountId));

        var login = await DispatchAsync("Account_20151111", "Login", new
        {
            email,
            password
        });

        Assert.Equal(200, login.StatusCode);
        using var loginPayload = JsonDocument.Parse(login.BodyText);
        Assert.Equal(accountId, loginPayload.RootElement.GetProperty("id").GetString());

        var loops = await DispatchAsync("Loop_20160324", "ListLoops", new { });
        Assert.Equal(200, loops.StatusCode);
        using var loopsPayload = JsonDocument.Parse(loops.BodyText);
        var loopsArray = loopsPayload.RootElement.EnumerateArray().ToArray();
        Assert.NotEmpty(loopsArray);
        var loopId = loopsArray[0].GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(loopId));

        var members = await DispatchAsync("Loop_20160324", "ListMembers", new
        {
            loopId
        });

        Assert.Equal(200, members.StatusCode);
        using var membersPayload = JsonDocument.Parse(members.BodyText);
        Assert.Equal(JsonValueKind.Array, membersPayload.RootElement.ValueKind);

        var prepare = await DispatchAsync("OOBE_20161026", "PrepareRobot", new
        {
            loopId,
            accountId,
            rollbackSnapshotId = "smoke-rollback-snapshot"
        });

        Assert.Equal(200, prepare.StatusCode);
        using var preparePayload = JsonDocument.Parse(prepare.BodyText);
        var token = preparePayload.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var beforeSetup = await DispatchAsync("OOBE_20161026", "GetStatus", new
        {
            token
        });

        Assert.Equal(200, beforeSetup.StatusCode);
        using var beforePayload = JsonDocument.Parse(beforeSetup.BodyText);
        Assert.False(beforePayload.RootElement.GetProperty("complete").GetBoolean());

        var setup = await DispatchAsync("OOBE_20161026", "SetupRobot", new
        {
            token,
            id = robotId
        });

        Assert.Equal(200, setup.StatusCode);
        using var setupPayload = JsonDocument.Parse(setup.BodyText);
        Assert.False(setupPayload.RootElement.GetProperty("serviceMode").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(setupPayload.RootElement.GetProperty("accessKeyId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(setupPayload.RootElement.GetProperty("secretAccessKey").GetString()));

        var afterSetup = await DispatchAsync("OOBE_20161026", "GetStatus", new
        {
            token
        });

        Assert.Equal(200, afterSetup.StatusCode);
        using var afterPayload = JsonDocument.Parse(afterSetup.BodyText);
        Assert.True(afterPayload.RootElement.GetProperty("complete").GetBoolean());
    }

    private Task<ProtocolDispatchResult> DispatchAsync(
        string servicePrefix,
        string operation,
        object body)
    {
        return _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = servicePrefix,
            Operation = operation,
            BodyText = JsonSerializer.Serialize(body)
        });
    }
}
