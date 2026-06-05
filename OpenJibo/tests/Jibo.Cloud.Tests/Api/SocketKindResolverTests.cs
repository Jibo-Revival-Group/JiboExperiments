using Jibo.Cloud.Api.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jibo.Cloud.Tests.Api;

public sealed class SocketKindResolverTests
{
    [Theory]
    [InlineData("api-socket.jibo.com", "/", "api-socket")]
    [InlineData("neo-hub.jibo.com", "/", "neo-hub-listen")]
    [InlineData("neo-hub.jibo.com", "/v1/proactive", "neo-hub-proactive")]
    [InlineData("openjibo.com", "/", "openjibo")]
    [InlineData("openjibo.ai", "/", "openjibo")]
    [InlineData("localhost", "/", "openjibo")]
    [InlineData("custom.listen.example", "/", "neo-hub-listen")]
    public void Resolve_ReturnsExpectedSocketKind(string host, string path, string expected)
    {
        var result = SocketKindResolver.Resolve(host, new PathString(path));

        Assert.Equal(expected, result);
    }
}