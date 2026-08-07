using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class RobotCredentialSeedApplierTests
{
    [Fact]
    public void ApplyRobot_BindsAccessKeyFingerprintAndSeedsEditableMember()
    {
        var store = new InMemoryCloudStateStore();
        var accessKeyId = "1tTJVwomYgTchcUqV1bC";

        RobotCredentialSeedApplier.ApplyRobot(store, new RobotCredentialSeedEntry
        {
            RobotId = "Air-Degree-Lunch-Canvas",
            DeviceId = "BOJW-1000-0017-1009-0021",
            SerialNumber = "BOJW-1000-0017-1009-0021",
            AccessKeyId = accessKeyId,
            SecretAccessKey = "unused-for-binding"
        });

        var fingerprint = RobotCredentialSeedApplier.FingerprintAccessKeyId(accessKeyId);
        var device = store.FindDeviceByAwsCredentialFingerprint(fingerprint);
        Assert.NotNull(device);
        Assert.Equal("Air-Degree-Lunch-Canvas", device!.RobotId);
        Assert.Equal("BOJW-1000-0017-1009-0021", device.DeviceId);

        var loop = store.GetLoops().First(l =>
            string.Equals(l.RobotFriendlyId, "BOJW-1000-0017-1009-0021", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.RobotId, "Air-Degree-Lunch-Canvas", StringComparison.OrdinalIgnoreCase));
        var members = store.GetLoopMembers(loop.LoopId)
            .Where(member => member.Type.Equals("member", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Contains(members, member =>
            string.Equals(member.FirstName, "Demo", StringComparison.OrdinalIgnoreCase));
    }
}
