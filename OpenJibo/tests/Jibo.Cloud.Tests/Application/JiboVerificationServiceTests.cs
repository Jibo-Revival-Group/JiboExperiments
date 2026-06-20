using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Application;

public sealed class JiboVerificationServiceTests
{
    [Fact]
    public void StartVerification_ReturnsNotFound_WhenFriendlyNameMissing()
    {
        var service = new JiboVerificationService();
        var store = new InMemoryCloudStateStore();

        var result = service.StartVerification(store, "Missing Robot");

        Assert.False(result.Ok);
        Assert.Equal("No Jibo was found with that friendly name.", result.Error);
    }

    [Fact]
    public void Confirm_ReturnsToken_WhenCodeMatches()
    {
        var service = new JiboVerificationService();
        var store = new InMemoryCloudStateStore();
        var robot = store.GetRobot();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = robot.DeviceId,
            RobotId = robot.RobotId,
            FriendlyName = "Kitchen Jibo",
            FirmwareVersion = robot.FirmwareVersion,
            ApplicationVersion = robot.ApplicationVersion,
            HostMappings = new Dictionary<string, string>(robot.HostMappings, StringComparer.OrdinalIgnoreCase)
        });

        var started = service.StartVerification(store, "Kitchen Jibo");
        Assert.True(started.Ok);

        var code = service.GetPendingCodeForDevice(store.GetRobot().DeviceId);
        Assert.False(string.IsNullOrWhiteSpace(code));

        var confirmed = service.TryConfirm(started.SessionId!, code!);
        Assert.True(confirmed.Ok);
        Assert.False(string.IsNullOrWhiteSpace(confirmed.Token));

        var consumed = service.TryConsumeToken(confirmed.Token!);
        Assert.NotNull(consumed);
        Assert.Equal("Kitchen Jibo", consumed.FriendlyName);
    }
}
