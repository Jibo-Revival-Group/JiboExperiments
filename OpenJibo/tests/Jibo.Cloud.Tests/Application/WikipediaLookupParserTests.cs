using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class WikipediaLookupParserTests
{
    [Theory]
    [InlineData("who is James Garfield", "james garfield")]
    [InlineData("hey jibo who is James Garfield", "james garfield")]
    [InlineData("who was James Garfield?", "james garfield")]
    [InlineData("what is Jibo", "jibo")]
    [InlineData("what was the internet", "the internet")]
    [InlineData("Who is the 20th president", "the 20th president")]
    public void TryParse_WhoWhatLookup_ExtractsSubject(string transcript, string expectedSubject)
    {
        var parsed = WikipediaLookupParser.TryParse(transcript, out var subject);

        Assert.True(parsed);
        Assert.Equal(expectedSubject, subject);
    }

    [Theory]
    [InlineData("tell me a joke")]
    [InlineData("who is")]
    [InlineData("what is?")]
    [InlineData("how do you spell holiday")]
    [InlineData("define holiday")]
    public void TryParse_NonLookupRequests_ReturnFalse(string transcript)
    {
        var parsed = WikipediaLookupParser.TryParse(transcript, out var subject);

        Assert.False(parsed);
        Assert.Null(subject);
    }
}
