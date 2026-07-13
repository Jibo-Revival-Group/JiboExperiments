using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class DefineInteractionServiceTests
{
    [Fact]
    public async Task BuildDecisionAsync_DefineWord_ReturnsFormattedDefinition()
    {
        var service = CreateService(new StubWordDefinitionProvider(
            "A day on which a religious event or secular celebration is traditionally observed."));

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "hey jibo define holiday",
            NormalizedTranscript = "hey jibo define holiday"
        });

        Assert.Equal("define_word", decision.IntentName);
        Assert.Equal(
            "The definition is. A day on which a religious event or secular celebration is traditionally observed.",
            decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_DefineWordWithoutProvider_ReturnsNotFoundReply()
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "define holiday",
            NormalizedTranscript = "define holiday"
        });

        Assert.Equal("define_word", decision.IntentName);
        Assert.Equal("I couldn't find a definition for that word.", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_DefineWordWithoutWord_ReturnsClarification()
    {
        var service = CreateService(new StubWordDefinitionProvider("unused"));

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "define",
            NormalizedTranscript = "define"
        });

        Assert.Equal("define_word", decision.IntentName);
        Assert.Equal(
            "I didn't catch what word you wanted me to define. Can you ask me again with a hey jibo?",
            decision.ReplyText);
    }

    private static JiboInteractionService CreateService(IWordDefinitionProvider? wordDefinitionProvider = null)
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore(),
            wordDefinitionProvider: wordDefinitionProvider);
    }

    private sealed class StubWordDefinitionProvider(string? definition) : IWordDefinitionProvider
    {
        public Task<string?> GetDefinitionAsync(string word, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(definition);
        }
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];
    }
}
