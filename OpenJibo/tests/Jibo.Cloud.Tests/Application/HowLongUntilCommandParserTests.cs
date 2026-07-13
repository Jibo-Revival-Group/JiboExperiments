using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class HowLongUntilCommandParserTests
{
    [Theory]
    [InlineData("hey jibo how long until christmas", "christmas")]
    [InlineData("how long till christmas", "christmas")]
    [InlineData("how many days until thanksgiving", "thanksgiving")]
    [InlineData("days until monday", "monday")]
    [InlineData("time until april ninth", "april ninth")]
    public void TryParse_RecognizesCountdownPrefixes(string transcript, string expectedTarget)
    {
        Assert.True(HowLongUntilCommandParser.TryParse(transcript, out var targetPhrase));
        Assert.Equal(expectedTarget, targetPhrase);
    }

    [Theory]
    [InlineData("tell me a joke")]
    [InlineData("how long have you been awake")]
    public void TryParse_RejectsNonCountdownUtterances(string transcript)
    {
        Assert.False(HowLongUntilCommandParser.TryParse(transcript, out _));
    }
}
