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
            "According to wikipedia dot org. James Abram Garfield was the 20th president of the United States.",
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
            wikipediaSummaryProvider: new StubWikipediaSummaryProvider(
                WikipediaSummaryResult.NotFound()),
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
            wikipediaSummaryProvider: new StubWikipediaSummaryProvider(
                WikipediaSummaryResult.NotFound()),
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
            "According to wikipedia dot org. Mount Everest is Earth's highest mountain above sea level.",
            decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_WhoIsQuery_WithoutSearchConfigured_SaysCantFindAnything()
    {
        var service = CreateService(
            wikipediaSummaryProvider: new StubWikipediaSummaryProvider(
                WikipediaSummaryResult.NotFound()));

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "Who is Zzxxyyqq",
            NormalizedTranscript = "Who is Zzxxyyqq"
        });

        Assert.Equal("knowledge_search_not_found", decision.IntentName);
        Assert.Equal("I can't find anything.", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_WhoIsQuery_DoubleChecksWikipediaAfterSearchMiss()
    {
        var wikipedia = new SequencingWikipediaSummaryProvider(
            WikipediaSummaryResult.NotFound(),
            WikipediaSummaryResult.Found("James Abram Garfield was the 20th president of the United States."));
        var knowledgeSearch = new CapturingKnowledgeSearchService(
            KnowledgeSearchResult.NotFound(SearchBackendKind.Wolfram));
        var service = CreateService(
            wikipediaSummaryProvider: wikipedia,
            knowledgeSearchService: knowledgeSearch);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "Who is James Garfield",
            NormalizedTranscript = "Who is James Garfield"
        });

        Assert.Equal("knowledge_search", decision.IntentName);
        Assert.Equal(
            "According to wikipedia dot org. James Abram Garfield was the 20th president of the United States.",
            decision.ReplyText);
        Assert.Equal(2, wikipedia.CallCount);
        Assert.True(wikipedia.LastBypassCache);
        Assert.NotNull(knowledgeSearch.LastQuery);
    }

    [Fact]
    public async Task BuildDecisionAsync_WhoIsQuery_WikipediaUnavailableWithoutSearch_SaysSourcesAreDown()
    {
        var service = CreateService(
            wikipediaSummaryProvider: new StubWikipediaSummaryProvider(
                WikipediaSummaryResult.Unavailable()));

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "Who is James Garfield",
            NormalizedTranscript = "Who is James Garfield"
        });

        Assert.Equal("knowledge_search_unavailable", decision.IntentName);
        Assert.Equal(
            "Huh, it seems like my info sources are down. Try asking me again a little later.",
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

    private sealed class StubWikipediaSummaryProvider : IWikipediaSummaryProvider
    {
        private readonly WikipediaSummaryResult _result;

        public StubWikipediaSummaryProvider(string summary)
            : this(WikipediaSummaryResult.Found(summary))
        {
        }

        public StubWikipediaSummaryProvider(WikipediaSummaryResult result)
        {
            _result = result;
        }

        public Task<WikipediaSummaryResult> GetSummaryAsync(
            string subject,
            CancellationToken cancellationToken = default,
            bool bypassCache = false)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class SequencingWikipediaSummaryProvider(params WikipediaSummaryResult[] results)
        : IWikipediaSummaryProvider
    {
        private int _index;

        public int CallCount { get; private set; }

        public bool LastBypassCache { get; private set; }

        public Task<WikipediaSummaryResult> GetSummaryAsync(
            string subject,
            CancellationToken cancellationToken = default,
            bool bypassCache = false)
        {
            CallCount++;
            LastBypassCache = bypassCache;
            var result = _index < results.Length ? results[_index++] : results[^1];
            return Task.FromResult(result);
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
