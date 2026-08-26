using Jibo.Cloud.Api.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jibo.Cloud.Tests.Api;

public sealed class SocketKindResolverTests
{
    [Theory]
    [InlineData("api-socket.jibo.com", "/", "api-socket")]
    [InlineData("api-socket.jibo.com", "/token-Ghost-Instance-Onion-Silk-123", "api-socket")]
    [InlineData("open-jibo-socket.openjibo.com", "/token-abc", "api-socket")]
    [InlineData("open-jibo-socket.jibo.pro", "/token-abc", "api-socket")]
    [InlineData("neo-hub.jibo.com", "/", "neo-hub-listen")]
    [InlineData("neo-hub.jibo.com", "/v1/proactive", "neo-hub-proactive")]
    [InlineData("neo-hub.jibo.com", "/token-should-not-reclassify", "neo-hub-listen")]
    [InlineData("openjibo.com", "/", "openjibo")]
    [InlineData("openjibo.ai", "/", "openjibo")]
    [InlineData("localhost", "/", "openjibo")]
    [InlineData("localhost", "/v1/listen", "neo-hub-listen")]
    [InlineData("localhost", "/v1/listen/token-abc", "neo-hub-listen")]
    [InlineData("localhost", "/v1/proactive", "neo-hub-proactive")]
    [InlineData("localhost", "/v1/homeassistant/ws", "home-assistant")]
    [InlineData("localhost", "/token-Ghost-Instance-Onion-Silk-123", "api-socket")]
    [InlineData("custom.listen.example", "/", "neo-hub-listen")]
    [InlineData("192.168.7.142", "/v1/listen", "neo-hub-listen")]
    [InlineData("192.168.7.142", "/listen", "neo-hub-listen")]
    [InlineData("192.168.7.142", "/v1/proactive", "neo-hub-proactive")]
    [InlineData("192.168.7.142", "/token-Ghost-Instance-Onion-Silk-1777637264293", "api-socket")]
    [InlineData("192.168.7.142", "/token-Royal-Current-Sage-Canvas-1777637264293", "api-socket")]
    public void Resolve_ReturnsExpectedSocketKind(string host, string path, string expected)
    {
        var result = SocketKindResolver.Resolve(host, new PathString(path));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("token-Ghost-Instance-Onion-Silk-1777637264293", "Ghost-Instance-Onion-Silk")]
    [InlineData("token-Royal-Current-Sage-Canvas-1777637264293", "Royal-Current-Sage-Canvas")]
    [InlineData("token-myrobot-abcdef0123456789abcdef0123456789", "myrobot")]
    public void TryParseRobotIdFromToken_ExtractsFriendlyId(string token, string expectedRobotId)
    {
        Assert.True(WebSocketRequestCoordinator.TryParseRobotIdFromToken(token, out var robotId));
        Assert.Equal(expectedRobotId, robotId);
    }
}
