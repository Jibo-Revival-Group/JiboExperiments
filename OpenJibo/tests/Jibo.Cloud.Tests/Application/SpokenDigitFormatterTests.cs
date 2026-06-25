using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class SpokenDigitFormatterTests
{
    [Fact]
    public void Format_SpeaksDigitsAsWords()
    {
        Assert.Equal("eight zero four two", SpokenDigitFormatter.Format("8042"));
        Assert.Equal("one two three four", SpokenDigitFormatter.Format("1234"));
    }
}