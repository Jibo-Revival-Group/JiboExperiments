using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class WikipediaTitleSimilarityTests
{
    [Theory]
    [InlineData("James Garfield", "James A. Garfield")]
    [InlineData("James A. Garfield", "James Garfield")]
    [InlineData("Jibo", "Jibo")]
    [InlineData("Jibo", "Jibo (robot)")]
    [InlineData("Mount Everest", "Mount Everest")]
    public void IsCloseMatch_AcceptsSimilarTitles(string query, string title)
    {
        Assert.True(WikipediaTitleSimilarity.IsCloseMatch(query, title));
    }

    [Theory]
    [InlineData("Jibo", "Cynthia Breazeal")]
    [InlineData("the 20th president", "James A. Garfield")]
    [InlineData("20th president", "James A. Garfield")]
    [InlineData("James Garfield", "Abraham Lincoln")]
    public void IsCloseMatch_RejectsUnrelatedTitles(string query, string title)
    {
        Assert.False(WikipediaTitleSimilarity.IsCloseMatch(query, title));
    }
}
