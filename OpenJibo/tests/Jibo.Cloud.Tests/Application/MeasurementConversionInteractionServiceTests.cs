using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Conversions;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class MeasurementConversionInteractionServiceTests
{
    [Fact]
    public async Task BuildDecisionAsync_FeetPerMile_ReturnsExpectedReply()
    {
        var service = CreateService(LoadCatalog());

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "how many feet in a mile",
            NormalizedTranscript = "how many feet in a mile"
        });

        Assert.Equal("measurement_conversion", decision.IntentName);
        Assert.Equal("There are 5280 feet in one mile.", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_InchesPerFoot_ReturnsExpectedReply()
    {
        var service = CreateService(LoadCatalog());

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "hey jibo how many inches are in a foot",
            NormalizedTranscript = "hey jibo how many inches are in a foot"
        });

        Assert.Equal("measurement_conversion", decision.IntentName);
        Assert.Equal("There are 12 inches in one foot.", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_UnknownConversion_ReturnsClarification()
    {
        var service = CreateService(LoadCatalog());

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "how many miles in a foot",
            NormalizedTranscript = "how many miles in a foot"
        });

        Assert.Equal("measurement_conversion", decision.IntentName);
        Assert.Equal(
            "I don't know that conversion. Try asking how many inches are in a foot.",
            decision.ReplyText);
    }

    private static IMeasurementConversionCatalog LoadCatalog()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "Jibo.Cloud",
            "dotnet",
            "src",
            "Jibo.Cloud.Infrastructure",
            "Content",
            "MeasurementConversionCatalog.json"));
        return new MeasurementConversionCatalogLoader().LoadFromFile(catalogPath);
    }

    private static JiboInteractionService CreateService(IMeasurementConversionCatalog catalog)
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore(),
            measurementConversionCatalog: catalog);
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];
    }
}
