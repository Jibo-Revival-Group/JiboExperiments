using Jibo.Cloud.Application.Services;
using Microsoft.Extensions.Configuration;

namespace Jibo.Cloud.Tests.Application;

public sealed class PortalSessionServiceTests
{
    [Fact]
    public void RobotMergePreviewToken_BindsSessionRouteSnapshotAndExpiry()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-10T12:00:00Z"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenJibo:Portal:SessionSigningKey"] = "test-signing-key"
            })
            .Build();
        var service = new PortalSessionService(configuration, clock);
        var session = new PortalSessionService.PortalSession(
            "session-a", "portal-admin", "Administrator", clock.GetUtcNow().AddHours(1));
        var otherSession = session with { Token = "session-b" };

        var token = service.CreateRobotMergePreviewToken(session, "source", "target", "snapshot-a");

        Assert.True(service.TryValidateRobotMergePreviewToken(
            token, session, "SOURCE", "TARGET", "SNAPSHOT-A"));
        Assert.False(service.TryValidateRobotMergePreviewToken(
            token + "x", session, "source", "target", "snapshot-a"));
        Assert.False(service.TryValidateRobotMergePreviewToken(
            token, otherSession, "source", "target", "snapshot-a"));
        Assert.False(service.TryValidateRobotMergePreviewToken(
            token, session, "other-source", "target", "snapshot-a"));
        Assert.False(service.TryValidateRobotMergePreviewToken(
            token, session, "source", "other-target", "snapshot-a"));
        Assert.False(service.TryValidateRobotMergePreviewToken(
            token, session, "source", "target", "snapshot-b"));
        Assert.False(service.TryValidateRobotMergePreviewToken(
            service.CreateSession("admin", "Administrator").Token,
            session, "source", "target", "snapshot-a"));
        Assert.False(service.TryValidateRobotMergePreviewToken(
            service.CreateSession("admin", "Administrator").Token,
            session, "source", "target", "snapshot-a"));

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False(service.TryValidateRobotMergePreviewToken(
            token, session, "source", "target", "snapshot-a"));
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
