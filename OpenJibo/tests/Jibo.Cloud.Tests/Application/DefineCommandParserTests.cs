using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class DefineCommandParserTests
{
    [Theory]
    [InlineData("define holiday", "holiday")]
    [InlineData("hey jibo define holiday", "holiday")]
    [InlineData("define the word holiday", "holiday")]
    [InlineData("what does ice cream mean", "ice cream")]
    [InlineData("what is the definition of holiday", "holiday")]
    [InlineData("what's the definition of holiday", "holiday")]
    [InlineData("define holiday?", "holiday")]
    public void TryParse_DefineRequests_ExtractWord(string transcript, string expectedWord)
    {
        var parsed = DefineCommandParser.TryParse(transcript, out var word);

        Assert.True(parsed);
        Assert.Equal(expectedWord, word);
    }

    [Theory]
    [InlineData("define")]
    [InlineData("define?")]
    public void TryParse_DefineRequestsWithoutWord_ReturnTrueWithNullWord(string transcript)
    {
        var parsed = DefineCommandParser.TryParse(transcript, out var word);

        Assert.True(parsed);
        Assert.Null(word);
    }

    [Theory]
    [InlineData("tell me a joke")]
    [InlineData("how do you spell holiday")]
    [InlineData("what's the weather")]
    public void TryParse_NonDefineRequests_ReturnFalse(string transcript)
    {
        var parsed = DefineCommandParser.TryParse(transcript, out var word);

        Assert.False(parsed);
        Assert.Null(word);
    }
}
