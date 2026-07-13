using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class SpellInteractionServiceTests
{
    [Fact]
    public async Task BuildDecisionAsync_SpellWord_ReturnsPhoneticSpelling()
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "hey jibo how do you spell attacking",
            NormalizedTranscript = "hey jibo how do you spell attacking"
        });

        Assert.Equal("spell_word", decision.IntentName);
        Assert.Equal(
            "attacking is spelt as ae, tea, tea, ae, see, kay, eye, en, jee.",
            decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_SpellWordWithoutWord_ReturnsClarification()
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "how do you spell",
            NormalizedTranscript = "how do you spell"
        });

        Assert.Equal("spell_word", decision.IntentName);
        Assert.Equal(
            "I didn't catch what word you wanted me to spell. Can you ask me again with a hey jibo?",
            decision.ReplyText);
    }

    private static JiboInteractionService CreateService()
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore());
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];
    }
}
