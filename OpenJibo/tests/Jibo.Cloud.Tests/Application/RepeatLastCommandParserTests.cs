using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class RepeatLastCommandParserTests
{
    [Theory]
    [InlineData("do that again")]
    [InlineData("hey jibo do that again")]
    [InlineData("Hey Jibo, do that again!")]
    [InlineData("do it again")]
    [InlineData("do this again")]
    [InlineData("do the same thing again")]
    [InlineData("do the same again")]
    [InlineData("do that one more time")]
    [InlineData("can you do that again")]
    [InlineData("could you do it again please")]
    [InlineData("would you do that one more time")]
    [InlineData("repeat that")]
    [InlineData("repeat the last command")]
    [InlineData("same thing again")]
    [InlineData("one more time")]
    [InlineData("again")]
    public void IsRepeatRequest_RepeatPhrases_ReturnsTrue(string transcript)
    {
        Assert.True(RepeatLastCommandParser.IsRepeatRequest(transcript));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hey jibo")]
    [InlineData("tell me a joke about school again")]
    [InlineData("roll a dice again")]
    [InlineData("what time is it")]
    [InlineData("say that again")]
    [InlineData("do that dance")]
    [InlineData("play it again sam")]
    public void IsRepeatRequest_OtherPhrases_ReturnsFalse(string transcript)
    {
        Assert.False(RepeatLastCommandParser.IsRepeatRequest(transcript));
    }

    [Fact]
    public void IsRepeatRequest_NullTranscript_ReturnsFalse()
    {
        Assert.False(RepeatLastCommandParser.IsRepeatRequest(null));
    }
}
