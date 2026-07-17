using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Holidays;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class CountdownInteractionServiceTests
{
    [Fact]
    public async Task BuildDecisionAsync_Christmas_LaunchesClockHolidaySkill()
    {
        var service = CreateService(LoadCatalog());

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "hey jibo how long until christmas",
            NormalizedTranscript = "hey jibo how long until christmas",
            Attributes = new Dictionary<string, object?>
            {
                ["context"] = """{"runtime":{"location":{"iso":"2026-07-12T12:00:00-04:00"}}}"""
            }
        });

        Assert.Equal("countdown", decision.IntentName);
        Assert.Equal("@be/clock", decision.SkillName);
        Assert.NotNull(decision.SkillPayload);
        Assert.Equal("whenIsHoliday", decision.SkillPayload["clockIntent"]);
        Assert.Equal("clock", decision.SkillPayload["domain"]);
        Assert.Equal("christmas", decision.SkillPayload["holiday"]);
    }

    [Fact]
    public async Task BuildDecisionAsync_HowManyDaysUntilChristmas_LaunchesClockHolidaySkill()
    {
        var service = CreateService(LoadCatalog());

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "how many days until christmas",
            NormalizedTranscript = "how many days until christmas",
            Attributes = new Dictionary<string, object?>
            {
                ["context"] = """{"runtime":{"location":{"iso":"2026-07-12T12:00:00-04:00"}}}"""
            }
        });

        Assert.Equal("countdown", decision.IntentName);
        Assert.Equal("@be/clock", decision.SkillName);
        Assert.Equal("christmas", decision.SkillPayload!["holiday"]);
    }

    [Fact]
    public async Task BuildDecisionAsync_Monday_ReturnsDaysUntilNextMonday()
    {
        var service = CreateService(LoadCatalog());

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "how long until monday",
            NormalizedTranscript = "how long until monday",
            Attributes = new Dictionary<string, object?>
            {
                ["context"] = """{"runtime":{"location":{"iso":"2026-07-12T12:00:00-04:00"}}}"""
            }
        });

        Assert.Equal("countdown", decision.IntentName);
        Assert.Equal("Monday is in 1 day.", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_AprilNinth_ReturnsDaysUntilDate()
    {
        var service = CreateService(LoadCatalog());

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "how long until april ninth",
            NormalizedTranscript = "how long until april ninth",
            Attributes = new Dictionary<string, object?>
            {
                ["context"] = """{"runtime":{"location":{"iso":"2026-07-12T12:00:00-04:00"}}}"""
            }
        });

        Assert.Equal("countdown", decision.IntentName);
        Assert.Equal("April 9 is in 271 days.", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_UnknownTarget_ReturnsClarification()
    {
        var service = CreateService(LoadCatalog());

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "how long until something unknown",
            NormalizedTranscript = "how long until something unknown"
        });

        Assert.Equal("countdown", decision.IntentName);
        Assert.Equal(
            "I didn't catch what you're asking about. Try asking how long until Christmas.",
            decision.ReplyText);
    }

    private static IHolidayCountdownCatalog LoadCatalog()
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
            "HolidayCountdownCatalog.json"));
        return new HolidayCountdownCatalogLoader().LoadFromFile(catalogPath);
    }

    private static JiboInteractionService CreateService(IHolidayCountdownCatalog catalog)
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore(),
            holidayCountdownCatalog: catalog);
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];
    }
}
