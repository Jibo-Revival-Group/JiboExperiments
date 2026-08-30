using Jibo.Cloud.Api.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Jibo.Cloud.Tests.Api;

public sealed class WebSocketTransportPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("self-hosted-hybrid")]
    [InlineData("managed")]
    public void IsAllowed_RejectsPlainHttpOutsideIsolatedSelfHosting(string? deploymentMode)
    {
        var policy = CreatePolicy(deploymentMode);
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";

        Assert.False(policy.IsAllowed(context.Request));
    }

    [Fact]
    public void IsAllowed_RejectsPlainHttpInManagedModeByDefault()
    {
        var policy = CreatePolicy("managed");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";

        Assert.False(policy.IsAllowed(context.Request));
    }

    [Fact]
    public void IsAllowed_RejectsPlainHttpInManagedModeWhenSecurityIsDisabled()
    {
        var policy = CreatePolicy("managed", securityMode: "false");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";

        Assert.False(policy.IsAllowed(context.Request));
    }

    [Fact]
    public void IsAllowed_AllowsHttpOnlyForExplicitIsolatedSelfHosting()
    {
        var policy = CreatePolicy("self-hosted-isolated");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";

        Assert.True(policy.IsAllowed(context.Request));
    }

    [Fact]
    public void IsAllowed_RejectsPlainHttpInIsolatedModeWhenSecurityIsEnabled()
    {
        var policy = CreatePolicy("self-hosted-isolated", securityMode: "true");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";

        Assert.False(policy.IsAllowed(context.Request));
    }

    [Fact]
    public void IsAllowed_AllowsDirectHttpsOutsideIsolatedSelfHosting()
    {
        var policy = CreatePolicy("self-hosted-hybrid");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";

        Assert.True(policy.IsAllowed(context.Request));
    }

    [Fact]
    public void IsAllowed_DoesNotTrustForwardedProtoOutsideIdentifiedManagedRevision()
    {
        var policy = CreatePolicy("self-hosted-hybrid");
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        Assert.False(policy.IsAllowed(context.Request));
    }

    [Theory]
    [InlineData("https", true)]
    [InlineData("http", false)]
    [InlineData("https,http", false)]
    public void IsAllowed_TrustsOnlySingleHttpsForwardingValueInManagedContainerApp(
        string forwardedProto,
        bool expected)
    {
        var policy = CreatePolicy("managed", "api--revision-1");
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;

        Assert.Equal(expected, policy.IsAllowed(context.Request));
    }

    private static WebSocketTransportPolicy CreatePolicy(
        string? deploymentMode,
        string? containerAppRevision = null,
        string? securityMode = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenJibo:Deployment:Mode"] = deploymentMode,
                ["CONTAINER_APP_REVISION"] = containerAppRevision,
                ["OpenJibo:Security:Mode"] = securityMode
            })
            .Build();
        return new WebSocketTransportPolicy(configuration);
    }
}
