using System.Net;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Weather;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class OpenWeatherGeocodeCandidateTests
{
    [Theory]
    [InlineData("Athens Greece", "Athens,GR")]
    [InlineData("Athens, Greece", "Athens,GR")]
    [InlineData("Rio De Janeiro Brazil", "Rio De Janeiro,BR")]
    [InlineData("Paris United States", "Paris,US")]
    [InlineData("London UK", "London,GB")]
    [InlineData("New York", "New York")]
    [InlineData("Athens Georgia", "Athens Georgia")]
    [InlineData("Athens,GA", "Athens,GA")]
    [InlineData("Athens,GR", "Athens,GR")]
    [InlineData("Toronto", "Toronto")]
    public void NormalizeGeocodeQuery_PreservesPlaceAndResolvesKnownCountry(string query, string expected)
    {
        Assert.Equal(expected, OpenWeatherReportProvider.NormalizeGeocodeQuery(query).Query);
    }

    [Fact]
    public async Task GetReportAsync_CountryMismatch_DoesNotRequestWeatherForWrongCity()
    {
        var handler = new MismatchedCountryHandler();
        using var client = new HttpClient(handler);
        var provider = new OpenWeatherReportProvider(client, new OpenWeatherOptions { ApiKey = "test-key" },
            NullLogger<OpenWeatherReportProvider>.Instance);

        var snapshot = await provider.GetReportAsync(new WeatherReportRequest("Athens Greece", null, null, false, null));

        Assert.Null(snapshot);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/geo/1.0/direct", request.AbsolutePath);
        Assert.Contains("q=Athens,GR", WebUtility.UrlDecode(request.Query), StringComparison.Ordinal);
    }

    private sealed class MismatchedCountryHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"name":"Athens","country":"US","lat":33.95,"lon":-83.38}]""")
            });
        }
    }
}
