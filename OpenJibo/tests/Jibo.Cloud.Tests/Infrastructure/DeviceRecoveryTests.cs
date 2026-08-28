using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class DeviceRecoveryTests
{
    [Theory]
    [InlineData("bootstrap", "any-device", false)]
    [InlineData("deployment-smoke", "any-device", false)]
    [InlineData("browser-harness", "any-device", false)]
    [InlineData(null, "open-jibo-smoke-staging-primary", false)]
    [InlineData(null, "fake-jibo-browser", false)]
    [InlineData(null, "openjibo-bootstrap-default", false)]
    [InlineData("physical", "Royal-Current-Sage-Canvas", true)]
    [InlineData("unknown", "legacy-physical-device", true)]
    public void IsRecoverable_UsesCanonicalSourceAndReservedNamespaces(
        string? source, string deviceId, bool expected)
    {
        Assert.Equal(expected, RobotRegistrationSources.IsRecoverable(source, deviceId));
    }
}