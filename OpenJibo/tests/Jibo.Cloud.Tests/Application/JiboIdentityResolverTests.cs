using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class JiboIdentityResolverTests
{
    [Fact]
    public void Resolve_UsesRobotFriendlyId_WhenSessionOnlyHasSerialDeviceId()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = "Test Robot"
        });

        var (deviceId, friendlyId) = JiboIdentityResolver.Resolve(new TurnContext
        {
            DeviceId = "BOJW-1000-0017-0820-0020"
        }, store);

        Assert.Equal("BOJW-1000-0017-0820-0020", deviceId);
        Assert.Equal("Ghost-Instance-Onion-Silk", friendlyId);
    }

    [Fact]
    public void Resolve_MatchesRegisteredFriendlyId()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = "Test Robot"
        });

        var (deviceId, friendlyId) = JiboIdentityResolver.Resolve(new TurnContext
        {
            DeviceId = "Ghost-Instance-Onion-Silk"
        }, store);

        Assert.Equal("BOJW-1000-0017-0820-0020", deviceId);
        Assert.Equal("Ghost-Instance-Onion-Silk", friendlyId);
    }
}
