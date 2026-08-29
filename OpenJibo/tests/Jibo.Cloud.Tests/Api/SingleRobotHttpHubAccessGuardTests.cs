using System.Net;
using Jibo.Cloud.Api.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Jibo.Cloud.Tests.Api;

public sealed class SingleRobotHttpHubAccessGuardTests
{
    [Fact]
    public void TryAcquire_DeniesWhenCompatibilityModeIsDisabled()
    {
        var guard = new SingleRobotHttpHubAccessGuard(false);

        var result = guard.TryAcquire(CreateContext("10.0.0.80", "10.0.0.81"), "neo-hub-listen");

        Assert.False(result.IsAllowed);
        Assert.Equal("compatibility-mode-disabled", result.Reason);
    }

    [Fact]
    public void TryAcquire_DeniesOutsideIsolatedSelfHostedMode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenJibo:SelfHosted:AllowTokenlessSingleRobotHub"] = "true",
                ["OpenJibo:Deployment:Mode"] = "self-hosted-hybrid"
            })
            .Build();
        var guard = new SingleRobotHttpHubAccessGuard(configuration);

        var result = guard.TryAcquire(CreateContext("10.0.0.80", "10.0.0.81"), "neo-hub-listen");

        Assert.False(result.IsAllowed);
        Assert.Equal("not-isolated-self-hosted", result.Reason);
    }


    [Fact]
    public void TryAcquire_DeniesHttpsAndPublicEndpoints()
    {
        var guard = new SingleRobotHttpHubAccessGuard(true);

        var https = guard.TryAcquire(CreateContext("10.0.0.80", "10.0.0.81", "https"),
            "neo-hub-listen");
        var publicHost = guard.TryAcquire(CreateContext("203.0.113.10", "10.0.0.81"),
            "neo-hub-listen");
        var publicClient = guard.TryAcquire(CreateContext("10.0.0.80", "203.0.113.11"),
            "neo-hub-listen");
        var proxyContext = CreateContext("10.0.0.80", "10.0.0.81");
        proxyContext.Request.Headers["X-Forwarded-Proto"] = "https";
        var proxied = guard.TryAcquire(proxyContext, "neo-hub-listen");

        Assert.Equal("https-requires-token", https.Reason);
        Assert.Equal("host-is-not-private", publicHost.Reason);
        Assert.Equal("client-is-not-private", publicClient.Reason);
        Assert.Equal("proxy-forwarding-not-supported", proxied.Reason);
    }

    [Fact]
    public void TryAcquire_AllowsOneClientAddressUntilItsLastLeaseIsReleased()
    {
        var guard = new SingleRobotHttpHubAccessGuard(true);

        var first = guard.TryAcquire(CreateContext("10.0.0.80", "10.0.0.81"), "neo-hub-listen");
        var sameClient = guard.TryAcquire(CreateContext("10.0.0.80", "10.0.0.81", path: "/v1/proactive"),
            "neo-hub-proactive");
        var secondClient = guard.TryAcquire(CreateContext("10.0.0.80", "10.0.0.82"), "neo-hub-listen");
        var excessSameClient = guard.TryAcquire(CreateContext("10.0.0.80", "10.0.0.81"),
            "neo-hub-listen");

        Assert.True(first.IsAllowed);
        Assert.True(sameClient.IsAllowed);
        Assert.False(secondClient.IsAllowed);
        Assert.Equal("single-robot-client-already-active", secondClient.Reason);
        Assert.False(excessSameClient.IsAllowed);
        Assert.Equal("single-robot-connection-limit-reached", excessSameClient.Reason);

        first.Lease!.Dispose();
        Assert.False(guard.TryAcquire(CreateContext("10.0.0.80", "10.0.0.82"), "neo-hub-listen").IsAllowed);

        sameClient.Lease!.Dispose();
        var afterRelease = guard.TryAcquire(CreateContext("10.0.0.80", "10.0.0.82"), "neo-hub-listen");
        Assert.True(afterRelease.IsAllowed);
        afterRelease.Lease!.Dispose();
    }

    private static DefaultHttpContext CreateContext(
        string host,
        string remoteAddress,
        string scheme = "http",
        string path = "/v1/listen")
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Scheme = scheme;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        return context;
    }
}
