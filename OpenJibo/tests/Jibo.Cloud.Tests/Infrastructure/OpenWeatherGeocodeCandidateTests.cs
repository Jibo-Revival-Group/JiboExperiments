using Jibo.Cloud.Infrastructure.Weather;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class OpenWeatherGeocodeCandidateTests
{
    [Fact]
    public void BuildGeocodeCandidates_CityAndCountry_AddsCommaForm()
    {
        Assert.Equal(
            ["Athens Greece", "Athens,Greece", "Athens"],
            OpenWeatherReportProvider.BuildGeocodeCandidates("Athens Greece"));
    }

    [Fact]
    public void BuildGeocodeCandidates_ThreeWords_TriesBothSplits()
    {
        Assert.Equal(
            ["Rio De Janeiro Brazil", "Rio De Janeiro,Brazil", "Rio De,Janeiro Brazil", "Rio De Janeiro"],
            OpenWeatherReportProvider.BuildGeocodeCandidates("Rio De Janeiro Brazil"));
    }

    [Theory]
    [InlineData("Toronto")]
    [InlineData("Athens,GR")]
    public void BuildGeocodeCandidates_SingleWordOrCommaForm_KeepsAsIs(string query)
    {
        Assert.Equal([query], OpenWeatherReportProvider.BuildGeocodeCandidates(query));
    }
}
