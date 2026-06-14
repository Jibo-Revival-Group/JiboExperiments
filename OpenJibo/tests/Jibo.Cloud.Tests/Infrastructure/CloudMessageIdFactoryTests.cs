using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class CloudMessageIdFactoryTests
{
    [Fact]
    public void CreateHubMessageId_ReturnsPrefixedCompactGuid()
    {
        var id = CloudMessageIdFactory.CreateHubMessageId();

        Assert.StartsWith("mid-", id, StringComparison.Ordinal);
        Assert.True(Guid.TryParseExact(id["mid-".Length..], "N", out _));
    }

    [Fact]
    public void CreateProtocolId_ReturnsCompactGuid()
    {
        var id = CloudMessageIdFactory.CreateProtocolId();

        Assert.True(Guid.TryParseExact(id, "N", out _));
    }
}