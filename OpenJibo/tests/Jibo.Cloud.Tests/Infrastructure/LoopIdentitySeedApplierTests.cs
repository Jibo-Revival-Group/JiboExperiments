using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class LoopIdentitySeedApplierTests
{
    [Fact]
    public void Apply_Disabled_DoesNotTouchStore()
    {
        var store = new InMemoryCloudStateStore();
        var before = store.GetRobot().RobotId;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenJibo:Robot:RobotId"] = "cccccccccccccccccccccccc"
        }).Build();

        Assert.False(LoopIdentitySeedApplier.Apply(store, config, NullLogger.Instance));
        Assert.Equal(before, store.GetRobot().RobotId);
    }

    [Fact]
    public void Apply_SeedsDumpIdentity_IncludingPreferredLoopAndOwnerIds()
    {
        var store = new InMemoryCloudStateStore();
        // Portal-added person on the bootstrap/default loop should rematerialize with the stock id.
        store.AddLoopMember(
            "openjibo-default-loop",
            accountId: null,
            email: null,
            firstName: "Bob",
            lastName: "Ross",
            gender: "male",
            birthday: null,
            isChild: false,
            type: "member");

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenJibo:Loop:SeedIdentity"] = "true",
            ["OpenJibo:Robot:RobotId"] = "cccccccccccccccccccccccc",
            ["OpenJibo:Robot:FriendlyId"] = "Test-Robot-Friendly-Name",
            ["OpenJibo:Loop:LoopId"] = "aaaaaaaaaaaaaaaaaaaaaaaa",
            ["OpenJibo:Loop:OwnerAccountId"] = "bbbbbbbbbbbbbbbbbbbbbbbb",
            ["OpenJibo:Loop:Name"] = "Test Household Jibo"
        }).Build();

        Assert.True(LoopIdentitySeedApplier.Apply(store, config, NullLogger.Instance));

        var loop = store.GetLoops()
            .Single(item => item.LoopId == "aaaaaaaaaaaaaaaaaaaaaaaa");
        Assert.Equal("cccccccccccccccccccccccc", loop.RobotId);
        Assert.Equal("Test-Robot-Friendly-Name", loop.RobotFriendlyId);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbb", loop.OwnerAccountId);
        Assert.Equal("Test Household Jibo", loop.Name);

        var members = store.GetLoopMembers(loop.LoopId).ToArray();
        Assert.Contains(members, m =>
            string.Equals(m.Type, "owner", StringComparison.OrdinalIgnoreCase) &&
            m.AccountId == "bbbbbbbbbbbbbbbbbbbbbbbb");
        Assert.Contains(members, m =>
            string.Equals(m.Type, "robot", StringComparison.OrdinalIgnoreCase) &&
            m.AccountId == "cccccccccccccccccccccccc");
        Assert.Contains(members, m => m.FirstName == "Bob" && m.LastName == "Ross");
    }

    [Fact]
    public void AlignHouseholdIdentity_CreatesPreferredObjectIdWhenFresh()
    {
        var store = new InMemoryCloudStateStore();
        var loop = store.AlignHouseholdIdentity(
            robotId: "cccccccccccccccccccccccc",
            robotFriendlyId: "Test-Robot-Friendly-Name",
            preferredLoopId: "aaaaaaaaaaaaaaaaaaaaaaaa",
            ownerAccountId: "bbbbbbbbbbbbbbbbbbbbbbbb",
            loopName: "Test Household Jibo");

        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaa", loop.LoopId);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbb", loop.OwnerAccountId);
        Assert.Contains(
            store.GetLoopMembers(loop.LoopId),
            m => string.Equals(m.Type, "robot", StringComparison.OrdinalIgnoreCase) &&
                 m.AccountId == "cccccccccccccccccccccccc");
    }
}
