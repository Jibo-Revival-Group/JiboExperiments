using System.Net;
using System.Text;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.News;
using Jibo.Cloud.Infrastructure.Weather;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class ProviderCachingTests
{
    [Fact]
    public async Task OpenWeatherReportProvider_ReusesCachedWeatherAndGeocodeResponses()
    {
        var handler = new CountingHttpMessageHandler(message =>
        {
            var path = message.RequestUri?.AbsolutePath ?? string.Empty;
            return path switch
            {
                "/geo/1.0/direct" => JsonResponse(
                    """[{"name":"Boston","state":"Massachusetts","country":"US","lat":42.3601,"lon":-71.0589}]"""),
                "/data/3.0/onecall" => JsonResponse(
                    """
                    {
                        "lat":42.3601,
                        "lon":-71.0589,
                        "timezone":"America/New_York",
                        "current":{"dt":1710000000,"temp":70.2,"weather":[{"main":"Clouds","description":"overcast clouds"}]},
                        "daily":[
                            {"dt":1710000000,"temp":{"day":70.2,"min":66.1,"max":72.9},"weather":[{"main":"Clouds","description":"overcast clouds"}]}
                        ]
                    }
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var provider = new OpenWeatherReportProvider(
            new HttpClient(handler),
            new OpenWeatherOptions
            {
                ApiKey = "test-key",
                CurrentCacheTtlSeconds = 300,
                GeocodeCacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 30
            },
            NullLogger<OpenWeatherReportProvider>.Instance);

        var request = new WeatherReportRequest("Boston,US", null, null, false, false, 0);
        var first = await provider.GetReportAsync(request);
        var second = await provider.GetReportAsync(request);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, handler.GetCallCount("/geo/1.0/direct"));
        Assert.Equal(1, handler.GetCallCount("/data/3.0/onecall"));
    }

    [Fact]
    public async Task OpenWeatherReportProvider_UsesCurrentHiLoForCurrentDay_WhenCurrentBandDiffers()
    {
        var offset = TimeSpan.FromHours(-5);
        var localNow = DateTimeOffset.UtcNow.ToOffset(offset);
        var todayNoon = new DateTimeOffset(localNow.Date.AddHours(12), offset);
        var handler = new CountingHttpMessageHandler(message =>
        {
            var path = message.RequestUri?.AbsolutePath ?? string.Empty;
            return path switch
            {
                "/geo/1.0/direct" => JsonResponse(
                    """[{"name":"Lone Jack","country":"US","lat":38.8708,"lon":-94.1733}]"""),
                "/data/3.0/onecall" => JsonResponse(
                    """
                    {
                        "lat":38.8708,
                        "lon":-94.1733,
                        "timezone":"America/Chicago",
                        "current":{"dt":1710000000,"temp":83.0,"weather":[{"main":"Clouds","description":"overcast clouds"}]},
                        "daily":[
                            {"dt":1710000000,"temp":{"day":83.0,"min":82.0,"max":83.0},"weather":[{"main":"Clouds","description":"overcast clouds"}]}
                        ]
                    }
                    """),
                "/data/2.5/forecast" => JsonResponse(
                    BuildForecastResponseJson(
                        "Lone Jack",
                        "US",
                        -18000,
                        [(todayNoon, 82, 82, 78, "Clouds", "overcast clouds")])),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var provider = new OpenWeatherReportProvider(
            new HttpClient(handler),
            new OpenWeatherOptions
            {
                ApiKey = "test-key",
                CurrentCacheTtlSeconds = 300,
                ForecastCacheTtlSeconds = 300,
                GeocodeCacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 30
            },
            NullLogger<OpenWeatherReportProvider>.Instance);

        var report =
            await provider.GetReportAsync(new WeatherReportRequest("Lone Jack,US", null, null, false, false, 0));

        Assert.NotNull(report);
        Assert.Equal(83, report!.Temperature);
        Assert.Equal(82, report.HighTemperature);
        Assert.Equal(78, report.LowTemperature);
        Assert.Equal(1, handler.GetCallCount("/data/3.0/onecall"));
        Assert.Equal(1, handler.GetCallCount("/data/2.5/forecast"));
    }

    [Fact]
    public async Task OpenWeatherReportProvider_UsesOneCallDailyForecastForTomorrow()
    {
        var handler = new CountingHttpMessageHandler(message =>
        {
            var path = message.RequestUri?.AbsolutePath ?? string.Empty;
            return path switch
            {
                "/geo/1.0/direct" => JsonResponse(
                    """[{"name":"Chicago","country":"US","lat":41.8781,"lon":-87.6298}]"""),
                "/data/3.0/onecall" => JsonResponse(
                    """
                    {
                        "lat":41.8781,
                        "lon":-87.6298,
                        "timezone":"America/Chicago",
                        "current":{"dt":1710000000,"temp":61.0,"weather":[{"main":"Clouds","description":"scattered clouds"}]},
                        "daily":[
                            {"dt":1710000000,"temp":{"day":61.0,"min":55.0,"max":66.0},"weather":[{"main":"Clouds","description":"scattered clouds"}]},
                            {"dt":1710086400,"temp":{"day":74.0,"min":60.0,"max":76.0},"summary":"A warmer day ahead","weather":[{"main":"Rain","description":"light rain"}]}
                        ]
                    }
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var provider = new OpenWeatherReportProvider(
            new HttpClient(handler),
            new OpenWeatherOptions
            {
                ApiKey = "test-key",
                CurrentCacheTtlSeconds = 300,
                ForecastCacheTtlSeconds = 300,
                GeocodeCacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 30
            },
            NullLogger<OpenWeatherReportProvider>.Instance);

        var report = await provider.GetReportAsync(new WeatherReportRequest("Chicago,US", null, null, true, false, 1));

        Assert.NotNull(report);
        Assert.Equal(74, report!.Temperature);
        Assert.Equal(76, report.HighTemperature);
        Assert.Equal(60, report.LowTemperature);
        Assert.Equal(1, handler.GetCallCount("/geo/1.0/direct"));
        Assert.Equal(1, handler.GetCallCount("/data/3.0/onecall"));
    }

    [Fact]
    public async Task OpenWeatherReportProvider_FallsBackToLegacyWeatherWhenOneCallIsUnauthorized()
    {
        var offset = TimeSpan.FromHours(-5);
        var localNow = DateTimeOffset.UtcNow.ToOffset(offset);
        var todayNoon = new DateTimeOffset(localNow.Date.AddHours(12), offset);
        var handler = new CountingHttpMessageHandler(message =>
        {
            var path = message.RequestUri?.AbsolutePath ?? string.Empty;
            return path switch
            {
                "/geo/1.0/direct" => JsonResponse(
                    """[{"name":"Boston","state":"Massachusetts","country":"US","lat":42.3601,"lon":-71.0589}]"""),
                "/data/3.0/onecall" => new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(
                        """{"cod":401,"message":"One Call 3.0 requires a subscription"}""",
                        Encoding.UTF8,
                        "application/json")
                },
                "/data/2.5/weather" => JsonResponse(
                    """{"name":"Boston","weather":[{"main":"Clouds","description":"overcast clouds"}],"main":{"temp":70.0,"temp_max":83.0,"temp_min":82.0}}"""),
                "/data/2.5/forecast" => JsonResponse(
                    BuildForecastResponseJson(
                        "Boston",
                        "US",
                        -18000,
                        [(todayNoon, 70, 72, 66, "Clouds", "overcast clouds")])),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var provider = new OpenWeatherReportProvider(
            new HttpClient(handler),
            new OpenWeatherOptions
            {
                ApiKey = "test-key",
                CurrentCacheTtlSeconds = 300,
                ForecastCacheTtlSeconds = 300,
                GeocodeCacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 30
            },
            NullLogger<OpenWeatherReportProvider>.Instance);

        var report = await provider.GetReportAsync(new WeatherReportRequest("Boston,US", null, null, false, false, 0));

        Assert.NotNull(report);
        Assert.Equal(70, report!.Temperature);
        Assert.Equal(72, report.HighTemperature);
        Assert.Equal(66, report.LowTemperature);
        Assert.Equal(1, handler.GetCallCount("/data/3.0/onecall"));
        Assert.Equal(1, handler.GetCallCount("/data/2.5/weather"));
        Assert.Equal(1, handler.GetCallCount("/data/2.5/forecast"));
    }

    [Fact]
    public async Task NewsApiBriefingProvider_ReusesCachedHeadlinesForIdenticalRequests()
    {
        var missingUserAgentRequestCount = 0;
        var handler = new CountingHttpMessageHandler(message =>
        {
            if (!message.Headers.TryGetValues("User-Agent", out var userAgents) ||
                !userAgents.Any())
                missingUserAgentRequestCount += 1;

            var path = message.RequestUri?.AbsolutePath ?? string.Empty;
            return path switch
            {
                "/v2/top-headlines" => JsonResponse(
                    """{"status":"ok","articles":[{"title":"Robotics team wins regional title","description":"A big local victory.","source":{"name":"AP News"},"url":"https://example.com/a"}]}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var provider = new NewsApiBriefingProvider(
            new HttpClient(handler),
            new NewsApiOptions
            {
                ApiKey = "test-key",
                CacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 30
            },
            NullLogger<NewsApiBriefingProvider>.Instance);

        var request = new NewsBriefingRequest(["sports"]);
        var first = await provider.GetBriefingAsync(request);
        var second = await provider.GetBriefingAsync(request);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, handler.GetCallCount("/v2/top-headlines"));
        Assert.Equal(0, missingUserAgentRequestCount);
    }

    [Fact]
    public async Task NewsApiBriefingProvider_FallsBackToUncategorizedHeadlines_WhenCategoryReturnsEmpty()
    {
        var handler = new CountingHttpMessageHandler(message =>
        {
            var path = message.RequestUri?.AbsolutePath ?? string.Empty;
            if (!string.Equals(path, "/v2/top-headlines", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var query = message.RequestUri?.Query ?? string.Empty;
            return JsonResponse(query.Contains("category=sports", StringComparison.OrdinalIgnoreCase)
                ? """{"status":"ok","articles":[]}"""
                : """{"status":"ok","articles":[{"title":"General robotics update","description":"Top story","source":{"name":"AP News"},"url":"https://example.com/general"}]}""");
        });
        var provider = new NewsApiBriefingProvider(
            new HttpClient(handler),
            new NewsApiOptions
            {
                ApiKey = "test-key",
                CacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 30
            },
            NullLogger<NewsApiBriefingProvider>.Instance);

        var result = await provider.GetBriefingAsync(new NewsBriefingRequest(["sports"]));

        Assert.NotNull(result);
        Assert.Single(result!.Headlines);
        Assert.Equal("General robotics update", result.Headlines[0].Title);
        Assert.Equal(2, handler.GetCallCount("/v2/top-headlines"));
    }

    [Fact]
    public async Task NewsApiBriefingProvider_FallsBackToEverything_WhenTopHeadlinesAreEmpty()
    {
        var handler = new CountingHttpMessageHandler(message =>
        {
            var path = message.RequestUri?.AbsolutePath ?? string.Empty;
            return path switch
            {
                "/v2/top-headlines" => JsonResponse("""{"status":"ok","articles":[]}"""),
                "/v2/everything" => JsonResponse(
                    """{"status":"ok","articles":[{"title":"Robotics breakthrough announced","description":"Lab unveils a new platform.","source":{"name":"Science Daily"},"url":"https://example.com/robotics"}]}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var provider = new NewsApiBriefingProvider(
            new HttpClient(handler),
            new NewsApiOptions
            {
                ApiKey = "test-key",
                DefaultCategories = ["general"],
                CacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 30
            },
            NullLogger<NewsApiBriefingProvider>.Instance);

        var result = await provider.GetBriefingAsync(new NewsBriefingRequest([]));

        Assert.NotNull(result);
        Assert.Single(result!.Headlines);
        Assert.Equal("Robotics breakthrough announced", result.Headlines[0].Title);
        Assert.Equal(2, handler.GetCallCount("/v2/top-headlines"));
        Assert.Equal(1, handler.GetCallCount("/v2/everything"));
    }

    [Fact]
    public async Task NewsApiBriefingProvider_ContinuesFallbackChain_WhenCategoryReturnsHttpError()
    {
        var handler = new CountingHttpMessageHandler(message =>
        {
            var path = message.RequestUri?.AbsolutePath ?? string.Empty;
            if (!string.Equals(path, "/v2/top-headlines", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var query = message.RequestUri?.Query ?? string.Empty;
            if (query.Contains("category=sports", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"status":"error","code":"parameterInvalid","message":"Category not supported for this key."}""",
                        Encoding.UTF8,
                        "application/json")
                };

            return JsonResponse(
                """{"status":"ok","articles":[{"title":"General robotics update","description":"Top story","source":{"name":"AP News"},"url":"https://example.com/general"}]}""");
        });
        var provider = new NewsApiBriefingProvider(
            new HttpClient(handler),
            new NewsApiOptions
            {
                ApiKey = "test-key",
                CacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 30
            },
            NullLogger<NewsApiBriefingProvider>.Instance);

        var result = await provider.GetBriefingAsync(new NewsBriefingRequest(["sports"]));

        Assert.NotNull(result);
        Assert.Single(result!.Headlines);
        Assert.Equal("General robotics update", result.Headlines[0].Title);
        Assert.Equal("success", result.ProviderStatus);
        Assert.Equal(2, handler.GetCallCount("/v2/top-headlines"));
    }

    [Fact]
    public async Task NewsApiBriefingProvider_PropagatesApiErrorCodeAndMessage_WhenAllEndpointsFail()
    {
        var handler = new CountingHttpMessageHandler(message =>
        {
            var path = message.RequestUri?.AbsolutePath ?? string.Empty;
            if (string.Equals(path, "/v2/top-headlines", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"status":"error","code":"parameterInvalid","message":"Category 'general' is not available for this account."}""",
                        Encoding.UTF8,
                        "application/json")
                };

            if (string.Equals(path, "/v2/everything", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"status":"error","code":"parametersMissing","message":"Missing required search query."}""",
                        Encoding.UTF8,
                        "application/json")
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var provider = new NewsApiBriefingProvider(
            new HttpClient(handler),
            new NewsApiOptions
            {
                ApiKey = "test-key",
                DefaultCategories = ["general"],
                CacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 30
            },
            NullLogger<NewsApiBriefingProvider>.Instance);

        var result = await provider.GetBriefingAsync(new NewsBriefingRequest([]));

        Assert.NotNull(result);
        Assert.Empty(result!.Headlines);
        Assert.Equal("http_error", result.ProviderStatus);
        Assert.Equal("parameterInvalid", result.ProviderErrorCode);
        Assert.Equal("Category 'general' is not available for this account.", result.ProviderMessage);
        Assert.Equal((int)HttpStatusCode.BadRequest, result.ProviderHttpStatusCode);
        Assert.Contains("/v2/top-headlines", result.ProviderEndpoint, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static string BuildForecastResponseJson(
        string cityName,
        string country,
        int timezoneSeconds,
        IReadOnlyList<(DateTimeOffset Timestamp, int Temp, int High, int Low, string Main, string Description)> entries)
    {
        var list = string.Join(
            ",",
            entries.Select(entry =>
                $$"""{"dt":{{entry.Timestamp.ToUnixTimeSeconds()}},"main":{"temp":{{entry.Temp}},"temp_min":{{entry.Low}},"temp_max":{{entry.High}}},"weather":[{"main":"{{entry.Main}}","description":"{{entry.Description}}"}]}"""));

        return $$"""{"city":{"name":"{{cityName}}","country":"{{country}}","timezone":{{timezoneSeconds}}},"list":[{{list}}]}""";
    }

    private sealed class CountingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        private readonly Dictionary<string, int> _callsByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _gate = new();

        public int GetCallCount(string path)
        {
            lock (_gate)
            {
                return _callsByPath.GetValueOrDefault(path, 0);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            lock (_gate)
            {
                _callsByPath[path] = _callsByPath.TryGetValue(path, out var count) ? count + 1 : 1;
            }

            return Task.FromResult(responseFactory(request));
        }
    }
}
