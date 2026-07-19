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
}
