using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class CountdownSpokenReplyFormatterTests
{
    [Fact]
    public void Format_ZeroDays_UsesToday()
    {
        Assert.Equal("Christmas Day is today.", CountdownSpokenReplyFormatter.Format("Christmas Day", 0));
    }

    [Fact]
    public void Format_OneDay_UsesSingular()
    {
        Assert.Equal("Monday is in 1 day.", CountdownSpokenReplyFormatter.Format("Monday", 1));
    }

    [Fact]
    public void Format_MultipleDays_UsesPlural()
    {
        Assert.Equal("Christmas Day is in 166 days.", CountdownSpokenReplyFormatter.Format("Christmas Day", 166));
    }
}
