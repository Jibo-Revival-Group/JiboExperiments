using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class WikipediaInteractionServiceTests
{
    [Fact]
    public async Task BuildDecisionAsync_WhoIsNamedEntity_UsesWikipediaWhenTitleMatches()
    {
        var service = CreateService(
            wikipediaSummaryProvider: new StubWikipediaSummaryProvider(
                "James Abram Garfield was the 20th president of the United States."),
            knowledgeSearchService: new StubKnowledgeSearchService(
                new KnowledgeSearchResult("Should not be used.", SearchBackendKind.Wolfram)));

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "Who is James Garfield",
            NormalizedTranscript = "Who is James Garfield"
        });

        Assert.Equal("knowledge_search", decision.IntentName);
        Assert.Equal(
            "According to wikipedia. James Abram Garfield was the 20th president of the United States.",
            decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_WhoIsOrdinal_FallsThroughToKnowledgeSearch()
    {
        var knowledgeSearch = new CapturingKnowledgeSearchService(
            new KnowledgeSearchResult(
                "The 20th president of the United States was James Garfield.",
                SearchBackendKind.Wolfram));
        var service = CreateService(
            wikipediaSummaryProvider: new StubWikipediaSummaryProvider(null),
            knowledgeSearchService: knowledgeSearch);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "Who is the 20th president",
            NormalizedTranscript = "Who is the 20th president"
        });

        Assert.Equal("knowledge_search", decision.IntentName);
        Assert.StartsWith("According to wolf ram alpha.", decision.ReplyText, StringComparison.Ordinal);
        Assert.Contains("James Garfield", decision.ReplyText, StringComparison.Ordinal);
        Assert.Equal("who is the 20th president", knowledgeSearch.LastQuery);
    }

    [Fact]
    public async Task BuildDecisionAsync_WhoIsJibo_FallsThroughWhenWikipediaRejectsTitle()
    {
        var knowledgeSearch = new CapturingKnowledgeSearchService(
            new KnowledgeSearchResult("Jibo was a social home robot.", SearchBackendKind.ChatGPT));
        var service = CreateService(
            wikipediaSummaryProvider: new StubWikipediaSummaryProvider(null),
            knowledgeSearchService: knowledgeSearch);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "Who is Jibo",
            NormalizedTranscript = "Who is Jibo"
        });

        Assert.Equal("knowledge_search", decision.IntentName);
        Assert.StartsWith("According to chat gee pee tee.", decision.ReplyText, StringComparison.Ordinal);
        Assert.NotNull(knowledgeSearch.LastQuery);
    }

    [Fact]
    public async Task BuildDecisionAsync_WikipediaWorksWithoutKnowledgeSearchConfigured()
    {
        var service = CreateService(
            wikipediaSummaryProvider: new StubWikipediaSummaryProvider(
                "Mount Everest is Earth's highest mountain above sea level."));

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "What is Mount Everest",
            NormalizedTranscript = "What is Mount Everest"
        });

        Assert.Equal("knowledge_search", decision.IntentName);
        Assert.Equal(
            "According to wikipedia. Mount Everest is Earth's highest mountain above sea level.",
            decision.ReplyText);
    }

    private static JiboInteractionService CreateService(
        IWikipediaSummaryProvider? wikipediaSummaryProvider = null,
        IKnowledgeSearchService? knowledgeSearchService = null)
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore(),
            wikipediaSummaryProvider: wikipediaSummaryProvider,
            knowledgeSearchService: knowledgeSearchService);
    }

    private sealed class StubWikipediaSummaryProvider(string? summary) : IWikipediaSummaryProvider
    {
        public Task<string?> GetSummaryAsync(string subject, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(summary);
        }
    }

    private sealed class StubKnowledgeSearchService(KnowledgeSearchResult? result) : IKnowledgeSearchService
    {
        public bool IsConfigured => true;

        public Task<KnowledgeSearchResult?> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingKnowledgeSearchService(KnowledgeSearchResult? result) : IKnowledgeSearchService
    {
        public bool IsConfigured => true;

        public string? LastQuery { get; private set; }

        public Task<KnowledgeSearchResult?> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(result);
        }
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];
    }
}
