using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class DiceRollInteractionServiceTests
{
    [Fact]
    public async Task BuildDecisionAsync_D6Roll_IncludesEsmlPayload()
    {
        var service = CreateService(new FixedIndexRandomizer(3));

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "hey jibo roll a dice",
            NormalizedTranscript = "hey jibo roll a dice"
        });

        Assert.Equal("roll_dice", decision.IntentName);
        Assert.Equal("It landed on 4.", decision.ReplyText);
        Assert.Equal("chitchat-skill", decision.SkillName);
        Assert.NotNull(decision.SkillPayload);
        Assert.Contains("roll-die-4", decision.SkillPayload!["esml"]?.ToString(), StringComparison.Ordinal);
        Assert.Equal("RA_JBO_RollOneDie", decision.SkillPayload["mim_id"]);
    }

    [Fact]
    public async Task BuildDecisionAsync_D20Roll_HasNoEsmlPayload()
    {
        var service = CreateService(new FixedIndexRandomizer(16));

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "roll a 20 sided dice",
            NormalizedTranscript = "roll a 20 sided dice"
        });

        Assert.Equal("roll_dice", decision.IntentName);
        Assert.Equal("It landed on 17.", decision.ReplyText);
        Assert.Null(decision.SkillName);
        Assert.Null(decision.SkillPayload);
    }

    [Fact]
    public async Task BuildDecisionAsync_D20CriticalFailure_ReturnsSpecialReply()
    {
        var service = CreateService(new FixedIndexRandomizer(0));

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "roll d20",
            NormalizedTranscript = "roll d20"
        });

        Assert.Equal("roll_dice", decision.IntentName);
        Assert.Equal("It landed on a 1. Critical failure!", decision.ReplyText);
        Assert.Null(decision.SkillPayload);
    }

    private static JiboInteractionService CreateService(IJiboRandomizer randomizer)
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            randomizer,
            new InMemoryPersonalMemoryStore());
    }

    private sealed class FixedIndexRandomizer(int index) : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[index];
    }
}
