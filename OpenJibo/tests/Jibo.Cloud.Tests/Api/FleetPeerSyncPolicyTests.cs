using Jibo.Cloud.Api.Hosting;
using Microsoft.Extensions.Configuration;

namespace Jibo.Cloud.Tests.Api;

public sealed class FleetPeerSyncPolicyTests
{
    [Fact]
    public void DefaultsToDisabledWithNoAllowedPeers()
    {
        var policy = FleetPeerSyncPolicy.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.False(policy.Enabled);
        Assert.Empty(policy.AllowedPeerHosts);
        Assert.False(policy.Allows("api.openjibo.com"));
    }

    [Fact]
    public void AllowsOnlyNormalizedExplicitHosts()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenJibo:FleetNetwork:PeerSyncEnabled"] = "true",
            ["OpenJibo:FleetNetwork:AllowedPeerHosts"] =
                " fleet.example.openjibo.com.;api.5x1.com,not a host "
        }).Build();

        var policy = FleetPeerSyncPolicy.FromConfiguration(configuration);

        Assert.True(policy.Enabled);
        Assert.Equal(2, policy.AllowedPeerHosts.Count);
        Assert.True(policy.Allows("FLEET.EXAMPLE.OPENJIBO.COM"));
        Assert.True(policy.Allows("api.5x1.com."));
        Assert.False(policy.Allows("api.openjibo.com"));
        Assert.False(policy.Allows("fleet.example.openjibo.com.attacker.test"));
    }
}
