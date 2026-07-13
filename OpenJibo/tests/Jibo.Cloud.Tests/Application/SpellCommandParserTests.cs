using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class SpellCommandParserTests
{
    [Theory]
    [InlineData("how do you spell attacking", "attacking")]
    [InlineData("hey jibo how do you spell attacking", "attacking")]
    [InlineData("spell cat", "cat")]
    [InlineData("can you spell dog", "dog")]
    [InlineData("could you spell fish", "fish")]
    [InlineData("how to spell robot", "robot")]
    [InlineData("how is attacking spelled", "attacking")]
    [InlineData("how is attacking spelt", "attacking")]
    [InlineData("what is the spelling of jibo", "jibo")]
    [InlineData("what's the spelling of jibo", "jibo")]
    [InlineData("how do you spell attacking?", "attacking")]
    public void TryParse_SpellRequests_ExtractWord(string transcript, string expectedWord)
    {
        var parsed = SpellCommandParser.TryParse(transcript, out var word);

        Assert.True(parsed);
        Assert.Equal(expectedWord, word);
    }

    [Theory]
    [InlineData("how do you spell")]
    [InlineData("how do you spell?")]
    [InlineData("spell")]
    public void TryParse_SpellRequestsWithoutWord_ReturnTrueWithNullWord(string transcript)
    {
        var parsed = SpellCommandParser.TryParse(transcript, out var word);

        Assert.True(parsed);
        Assert.Null(word);
    }

    [Theory]
    [InlineData("tell me a joke")]
    [InlineData("what's the weather")]
    [InlineData("how are you")]
    public void TryParse_NonSpellRequests_ReturnFalse(string transcript)
    {
        var parsed = SpellCommandParser.TryParse(transcript, out var word);

        Assert.False(parsed);
        Assert.Null(word);
    }
}
