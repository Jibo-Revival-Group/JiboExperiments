using Jibo.Cloud.Api.Hosting;

namespace Jibo.Cloud.Tests.Api;

public sealed class RobotDiagnosticBeaconStoreTests
{
    [Fact]
    public void PublishAndSnapshot_IsBoundedAndExpires()
    {
        var store = new RobotDiagnosticBeaconStore();
        var now = DateTimeOffset.UtcNow;

        store.Publish("Royal-Current-Sage-Canvas", Enumerable.Range(1, 600).Select(i => $"line-{i}"), now);

        var snapshot = store.Snapshot("Royal-Current-Sage-Canvas", now);
        Assert.Equal(500, snapshot.Count);
        Assert.Equal("line-101", snapshot[0]);
        Assert.Empty(store.Snapshot("Royal-Current-Sage-Canvas", now.AddMinutes(3)));
    }

    [Fact]
    public void GetActive_ReturnsOnlyFreshBeacons()
    {
        var store = new RobotDiagnosticBeaconStore();
        var now = DateTimeOffset.UtcNow;
        store.Publish("fresh", ["hello"], now);
        store.Publish("stale", ["old"], now.AddMinutes(-3));

        var active = store.GetActive(now);

        var beacon = Assert.Single(active);
        Assert.Equal("fresh", beacon.RobotId);
        Assert.Equal(1, beacon.LineCount);
    }
}
