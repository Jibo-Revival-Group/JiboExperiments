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

    [Fact]
    public void Resolve_DoesNotInheritSingletonDeviceId_ForUnregisteredFriendlyId()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "SHARED-SINGLETON-DEVICE",
            RobotId = "Bootstrap-Robot",
            FriendlyName = "Bootstrap"
        });

        var (deviceId, friendlyId) = JiboIdentityResolver.Resolve(new TurnContext
        {
            DeviceId = "Jibo-One"
        }, store);

        Assert.Equal("Jibo-One", deviceId);
        Assert.Equal("Jibo-One", friendlyId);
        Assert.NotEqual("SHARED-SINGLETON-DEVICE", deviceId);
    }

    [Fact]
    public void Resolve_PrefersContextGeneralRobotId_OverSingletonSessionDeviceId()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "SHARED-SINGLETON-DEVICE",
            RobotId = "Bootstrap-Robot",
            FriendlyName = "Bootstrap"
        });

        var (deviceId, friendlyId) = JiboIdentityResolver.Resolve(new TurnContext
        {
            DeviceId = "SHARED-SINGLETON-DEVICE",
            Attributes = new Dictionary<string, object?>
            {
                ["context"] = """{"general":{"accountID":"acct-1","robotID":"Ghost-Instance-Onion-Silk"}}"""
            }
        }, store);

        Assert.Equal("Ghost-Instance-Onion-Silk", deviceId);
        Assert.Equal("Ghost-Instance-Onion-Silk", friendlyId);
    }
}