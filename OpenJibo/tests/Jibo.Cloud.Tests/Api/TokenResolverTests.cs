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

    [Fact]
    public void Resolve_ReturnsNull_WhenNoTokenIsPresent()
    {
        var request = new DefaultHttpContext().Request;

        var token = TokenResolver.Resolve(request);

        Assert.Null(token);
    }
}