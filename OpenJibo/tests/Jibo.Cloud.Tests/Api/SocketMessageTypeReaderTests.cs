using Jibo.Cloud.Api.Hosting;

namespace Jibo.Cloud.Tests.Api;

public sealed class SocketMessageTypeReaderTests
{
    [Theory]
    [InlineData(null, "BINARY_OR_EMPTY")]
    [InlineData("", "BINARY_OR_EMPTY")]
    [InlineData("   ", "BINARY_OR_EMPTY")]
    [InlineData("""{"type":"HELLO"}""", "HELLO")]
    [InlineData("""{"other":"value"}""", "UNKNOWN")]
    [InlineData("not-json", "TEXT")]
    public void Read_ReturnsExpectedMessageType(string? text, string expected)
    {
        var result = SocketMessageTypeReader.Read(text);

        Assert.Equal(expected, result);
    }
}
