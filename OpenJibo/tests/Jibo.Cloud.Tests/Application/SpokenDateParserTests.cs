using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class SpokenDateParserTests
{
    [Theory]
    [InlineData("april ninth", "April 9", 4, 9)]
    [InlineData("december twenty fifth", "December 25", 12, 25)]
    [InlineData("january 1", "January 1", 1, 1)]
    [InlineData("the march third", "March 3", 3, 3)]
    public void TryParse_RecognizesSpokenAndNumericDates(string phrase, string expectedLabel, int month, int day)
    {
        Assert.True(SpokenDateParser.TryParse(phrase, out var label, out var parsedMonth, out var parsedDay));
        Assert.Equal(expectedLabel, label);
        Assert.Equal(month, parsedMonth);
        Assert.Equal(day, parsedDay);
    }

    [Theory]
    [InlineData("christmas")]
    [InlineData("april")]
    [InlineData("thirty second")]
    public void TryParse_RejectsInvalidDates(string phrase)
    {
        Assert.False(SpokenDateParser.TryParse(phrase, out _, out _, out _));
    }
}
