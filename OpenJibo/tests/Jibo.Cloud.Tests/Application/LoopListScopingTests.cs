using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace Jibo.Cloud.Tests.Application;

public sealed class LoopListScopingTests
{
    [Fact]
    public async Task LoopList_ReturnsOnlyCallerRobotLoop_WhenMultipleLoopsExist()
    {
        var store = new InMemoryCloudStateStore();
        var ghost = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        store.AddLoop(null, null, "Air-Degree-Lunch-Canvas", "BOJW-1000-0017-1009-0021");
        store.AddLoopMember(ghost.LoopId, null, null, "Portal", "Person", "unknown", null, false, "member");

        var service = new JiboCloudProtocolService(store, authHandler: new CloudAuthProtocolHandler(store));
        var result = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Loop_20160324",
            Operation = "List",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal(200, result.StatusCode);
        using var payload = JsonDocument.Parse(result.BodyText);
        var loops = payload.RootElement.EnumerateArray().ToArray();
        Assert.Single(loops);
        Assert.Equal(ghost.LoopId, loops[0].GetProperty("id").GetString());
        Assert.Contains(
            loops[0].GetProperty("members").EnumerateArray(),
            member => member.GetProperty("account").GetProperty("firstName").GetString() == "Portal");
        Assert.Contains(
            loops[0].GetProperty("members").EnumerateArray(),
            member => member.GetProperty("type").GetString() == "robot");
    }

    [Fact]
    public async Task LoopList_UsesConfiguredRobotId_AndIncludesRobotAccountId()
    {
        var store = new InMemoryCloudStateStore();
        var loop = store.AddLoop(
            "Ghost Loop",
            store.GetAccount().AccountId,
            "Ghost-Instance-Onion-Silk",
            "BOJW-1000-0017-0820-0020");
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = "Ghost-Instance-Onion-Silk"
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenJibo:Robot:RobotId"] = "5a0b6398faa0f0001c5d0df1"
            })
            .Build();

        var service = new JiboCloudProtocolService(
            store,
            authHandler: new CloudAuthProtocolHandler(store),
            configuration: configuration);

        await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Robot_20160225",
            Operation = "UpdateRobot",
            BodyText = """{"id":"Ghost-Instance-Onion-Silk","payload":{"serialNumber":"BOJW-1000-0017-0820-0020"}}"""
        });

        var result = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Loop_20160324",
            Operation = "List",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal(200, result.StatusCode);
        using var payload = JsonDocument.Parse(result.BodyText);
        var loops = payload.RootElement.EnumerateArray().ToArray();
        Assert.Single(loops);
        Assert.Equal(loop.LoopId, loops[0].GetProperty("id").GetString());
        Assert.Equal("5a0b6398faa0f0001c5d0df1", loops[0].GetProperty("robot").GetString());
        Assert.Equal("Ghost-Instance-Onion-Silk", loops[0].GetProperty("robotFriendlyId").GetString());

        var members = loops[0].GetProperty("members").EnumerateArray().ToArray();
        Assert.Contains(members, member =>
            member.GetProperty("type").GetString() == "robot" &&
            member.GetProperty("accountId").GetString() == "5a0b6398faa0f0001c5d0df1");
        Assert.Contains(members, member =>
            member.GetProperty("type").GetString() == "owner" &&
            member.GetProperty("status").GetString() == "accepted");
    }

    [Fact]
    public void ResolveLoopsForKeys_NeverReturnsMoreThanOne_WhenKeysOverlap()
    {
        var store = new InMemoryCloudStateStore();
        var preferred = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");
        var other = store.AddLoop(null, null, "Air-Degree-Lunch-Canvas", "BOJW-1000-0017-1009-0021");

        // Simulate historical key overlap by injecting a second match into the private loop list.
        var loopsField = typeof(InMemoryCloudStateStore)
            .GetField("_loops", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(loopsField);
        var loops = (System.Collections.IList)loopsField!.GetValue(store)!;
        loops.Add(new LoopRecord
        {
            LoopId = "loop-overlapping-ghost",
            Name = "Overlap",
            OwnerAccountId = store.GetAccount().AccountId,
            RobotId = "5a0b6398faa0f0001c5d0df1",
            RobotFriendlyId = "Ghost-Instance-Onion-Silk"
        });

        var keys = new[]
        {
            "Ghost-Instance-Onion-Silk",
            "BOJW-1000-0017-0820-0020",
            "5a0b6398faa0f0001c5d0df1",
            other.RobotId
        };
        var resolved = LoopRosterResolver.ResolveLoopsForKeys(
            store, keys, configuredRobotId: "5a0b6398faa0f0001c5d0df1");
        Assert.Single(resolved);
        Assert.Equal("loop-overlapping-ghost", resolved[0].LoopId);

        var byFriendly = LoopRosterResolver.ResolveLoopsForKeys(
            store,
            ["Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020", "5a0b6398faa0f0001c5d0df1"],
            configuredRobotId: null);
        Assert.Single(byFriendly);
        // Prefer Pegasus friendly match (preferred) over a secondary overlapping hex row.
        Assert.Equal(preferred.LoopId, byFriendly[0].LoopId);
        Assert.NotEqual(other.LoopId, byFriendly[0].LoopId);
    }

    [Fact]
    public void SelectSingleLoop_PrefersConfiguredRobotId()
    {
        var winner = LoopRosterResolver.SelectSingleLoop(
            [
                new LoopRecord { LoopId = "a", RobotId = "friendly-a", RobotFriendlyId = "Ghost-Instance-Onion-Silk" },
                new LoopRecord { LoopId = "b", RobotId = "5a0b6398faa0f0001c5d0df1", RobotFriendlyId = "Ghost-Instance-Onion-Silk" }
            ],
            configuredRobotId: "5a0b6398faa0f0001c5d0df1",
            callerKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ghost-Instance-Onion-Silk" });

        Assert.Equal("b", winner.LoopId);
    }
}
