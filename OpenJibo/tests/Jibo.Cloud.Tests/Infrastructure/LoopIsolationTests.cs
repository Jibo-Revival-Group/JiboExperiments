using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class LoopIsolationTests
{
    [Fact]
    public void AddLoop_DoesNotMergeDistinctFriendlyIds_WhenSecondArgIsShared()
    {
        var store = new InMemoryCloudStateStore();

        var first = store.AddLoop(null, null, "Jibo-One", "SHARED");
        var second = store.AddLoop(null, null, "Jibo-Two", "SHARED");

        Assert.NotEqual(first.LoopId, second.LoopId);
        Assert.Equal("Jibo-One", first.RobotId);
        Assert.Equal("Jibo-Two", second.RobotId);
    }

    [Fact]
    public void AddLoop_FindsExistingByFriendlyId_WhenBothArgsAreFriendlyId()
    {
        var store = new InMemoryCloudStateStore();

        var created = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "Ghost-Instance-Onion-Silk");
        var found = store.AddLoop(null, null, "Ghost-Instance-Onion-Silk", "Ghost-Instance-Onion-Silk");

        Assert.Equal(created.LoopId, found.LoopId);
    }

    [Fact]
    public void UpdateRobot_PromotesSeededLoopRobotId_AndPreservesFriendlyId()
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
            RobotId = "5a0b6398faa0f0001c5d0df1",
            FriendlyName = "Ghost-Instance-Onion-Silk"
        });

        var updated = store.GetLoops().Single(item => item.LoopId == loop.LoopId);
        Assert.Equal("5a0b6398faa0f0001c5d0df1", updated.RobotId);
        Assert.Equal("Ghost-Instance-Onion-Silk", updated.RobotFriendlyId);

        var robotMember = store.GetLoopMembers(loop.LoopId)
            .Single(member => member.Type == "robot");
        Assert.Equal("5a0b6398faa0f0001c5d0df1", robotMember.AccountId);
        Assert.Equal("accepted", robotMember.Status);
        Assert.True(string.IsNullOrWhiteSpace(robotMember.FirstName));
    }
}
