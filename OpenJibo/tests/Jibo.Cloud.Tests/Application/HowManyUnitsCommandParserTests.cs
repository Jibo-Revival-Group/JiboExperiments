using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class HowManyUnitsCommandParserTests
{
    [Theory]
    [InlineData("how many feet in a mile", "feet", "mile")]
    [InlineData("hey jibo how many inches are in a foot", "inches", "foot")]
    [InlineData("how many ounces in one pound", "ounces", "pound")]
    [InlineData("how many cups in gallon", "cups", "gallon")]
    public void TryParse_RecognizesConversionPhrases(string transcript, string expectedSmall, string expectedLarge)
    {
        Assert.True(HowManyUnitsCommandParser.TryParse(transcript, out var query));
        Assert.Equal(expectedSmall, query.SmallUnitPhrase);
        Assert.Equal(expectedLarge, query.LargeUnitPhrase);
    }

    [Theory]
    [InlineData("how many days until christmas")]
    [InlineData("how many people do you know")]
    [InlineData("what's 12 plus 8")]
    public void TryParse_RejectsNonConversionUtterances(string transcript)
    {
        Assert.False(HowManyUnitsCommandParser.TryParse(transcript, out _));
    }
}
