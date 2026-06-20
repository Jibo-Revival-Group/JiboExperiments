using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Application;

public sealed class JiboVerificationServiceTests
{
    [Fact]
    public void StartVerification_ReturnsNotFound_WhenFriendlyIdMissing()
    {
        var service = new JiboVerificationService();
        var store = new InMemoryCloudStateStore();

        var result = service.StartVerification(store, "Missing-Robot-Id-Here");

        Assert.False(result.Ok);
        Assert.Equal("No Jibo was found with that friendly ID.", result.Error);
    }

    [Fact]
    public void Confirm_ReturnsToken_WhenCodeMatches()
    {
        var service = new JiboVerificationService();
        var store = new InMemoryCloudStateStore();
        var robot = store.GetRobot();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = robot.FriendlyName,
            FirmwareVersion = robot.FirmwareVersion,
            ApplicationVersion = robot.ApplicationVersion,
            HostMappings = new Dictionary<string, string>(robot.HostMappings, StringComparer.OrdinalIgnoreCase)
        });

        var started = service.StartVerification(store, "Ghost-Instance-Onion-Silk");
        Assert.True(started.Ok);

        var code = service.GetPendingCodeForDevice("Ghost-Instance-Onion-Silk");
        Assert.False(string.IsNullOrWhiteSpace(code));

        var confirmed = service.TryConfirm(started.SessionId!, code!);
        Assert.True(confirmed.Ok);
        Assert.False(string.IsNullOrWhiteSpace(confirmed.Token));

        var consumed = service.TryConsumeToken(confirmed.Token!);
        Assert.NotNull(consumed);
        Assert.Equal("Ghost-Instance-Onion-Silk", consumed.FriendlyId);
    }
}
