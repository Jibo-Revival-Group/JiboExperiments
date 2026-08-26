using Jibo.Cloud.Api.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jibo.Cloud.Tests.Api;

public sealed class TokenResolverTests
{
    [Fact]
    public void Resolve_ReturnsBearerToken_WhenAuthorizationHeaderExists()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers.Authorization = "Bearer abc123";

        var token = TokenResolver.Resolve(request);

        Assert.Equal("abc123", token);
    }

    [Fact]
    public void Resolve_ReturnsPathToken_WhenPathContainsToken()
    {
        var request = new DefaultHttpContext().Request;
        request.Path = "/my-token";

        var token = TokenResolver.Resolve(request);

        Assert.Equal("my-token", token);
    }

    [Theory]
    [InlineData("/v1/listen/token-abc", "token-abc")]
    [InlineData("/listen/token-abc", "token-abc")]
    [InlineData("/v1/proactive/token-abc", "token-abc")]
    [InlineData("/proactive/token-abc", "token-abc")]
    public void Resolve_ReturnsTokenAfterHubRoute(string path, string expected)
    {
        var request = new DefaultHttpContext().Request;
        request.Path = path;

        var token = TokenResolver.Resolve(request);

        Assert.Equal(expected, token);
    }

    [Theory]
    [InlineData("/v1/listen")]
    [InlineData("/listen")]
    [InlineData("/v1/proactive")]
    [InlineData("/proactive")]
    public void Resolve_ReturnsNull_WhenHubRouteHasNoToken(string path)
    {
        var request = new DefaultHttpContext().Request;
        request.Path = path;

        var token = TokenResolver.Resolve(request);

        Assert.Null(token);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenNoTokenIsPresent()
    {
        var request = new DefaultHttpContext().Request;

        var token = TokenResolver.Resolve(request);

        Assert.Null(token);
    }
}