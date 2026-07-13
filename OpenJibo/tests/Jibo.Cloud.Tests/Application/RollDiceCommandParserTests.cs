using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class RollDiceCommandParserTests
{
    [Theory]
    [InlineData("roll a dice", 6)]
    [InlineData("roll a die", 6)]
    [InlineData("roll dice", 6)]
    [InlineData("hey jibo roll a dice", 6)]
    [InlineData("roll a 20 sided dice", 20)]
    [InlineData("roll a 20-sided die", 20)]
    [InlineData("roll d20", 20)]
    [InlineData("roll a d6", 6)]
    [InlineData("roll a twenty sided dice", 20)]
    public void TryParse_RecognizesDiceRollPhrases(string transcript, int expectedSides)
    {
        Assert.True(RollDiceCommandParser.TryParse(transcript, out var query));
        Assert.Equal(expectedSides, query.Sides);
    }

    [Theory]
    [InlineData("roll a 1 sided dice")]
    [InlineData("roll d101")]
    [InlineData("roll the dice down the hill")]
    [InlineData("how many days until christmas")]
    public void TryParse_RejectsInvalidOrNonDicePhrases(string transcript)
    {
        Assert.False(RollDiceCommandParser.TryParse(transcript, out _));
    }
}
