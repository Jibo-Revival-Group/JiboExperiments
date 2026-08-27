using Jibo.Cloud.Api.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jibo.Cloud.Tests.Api;

public sealed class RequestLogSanitizerTests
{
    [Theory]
    [InlineData("api-socket", "/secret-token", "/{notification-token}")]
    [InlineData("neo-hub-listen", "/v1/listen/secret-token", "/v1/listen/{token}")]
    [InlineData("neo-hub-proactive", "/v1/proactive/secret-token", "/v1/proactive/{token}")]
    [InlineData("home-assistant", "/api/home-assistant/socket", "/api/home-assistant/socket")]
    public void RedactWebSocketPath_RemovesCredentialSegments(string kind, string path, string expected)
    {
        var result = RequestLogSanitizer.RedactWebSocketPath(kind, new PathString(path));

        Assert.Equal(expected, result);
        Assert.DoesNotContain("secret-token", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactQuery_RemovesWebSocketQueryValues()
    {
        var query = new QueryString("?token=secret-token&mode=test");

        Assert.Equal("[redacted]", RequestLogSanitizer.RedactQuery(query, true));
        Assert.Equal(query.Value, RequestLogSanitizer.RedactQuery(query, false));
        Assert.Null(RequestLogSanitizer.RedactQuery(QueryString.Empty, true));
    }
}
