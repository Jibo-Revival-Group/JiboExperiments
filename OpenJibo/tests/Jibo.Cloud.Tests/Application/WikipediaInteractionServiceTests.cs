using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
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

    [Fact]
    public async Task BuildDecisionAsync_WikipediaLookup_PublishesThinkingBeforeSummaryReturns()
    {
        var progress = new CapturingTurnProgressPublisher();
        var wikipedia = new OrderingWikipediaSummaryProvider(
            "James Abram Garfield was the 20th president of the United States.",
            onLookup: () => progress.PublishCount);
        var service = CreateService(
            wikipediaSummaryProvider: wikipedia,
            turnProgressPublisher: progress);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "Who is James Garfield",
            NormalizedTranscript = "Who is James Garfield"
        });

        Assert.Equal("knowledge_search", decision.IntentName);
        Assert.Equal(1, progress.PublishCount);
        Assert.Equal(1, wikipedia.PublishCountSeenAtLookupStart);
        Assert.Contains("Thinking_01", progress.LastPayload, StringComparison.Ordinal);
        Assert.Contains("\"final\":false", progress.LastPayload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildDecisionAsync_KnowledgeSearch_PublishesThinkingBeforeBackendReturns()
    {
        var progress = new CapturingTurnProgressPublisher();
        var knowledgeSearch = new OrderingKnowledgeSearchService(
            new KnowledgeSearchResult("Forty two.", SearchBackendKind.Wolfram),
            onSearch: () => progress.PublishCount);
        var service = CreateService(
            knowledgeSearchService: knowledgeSearch,
            turnProgressPublisher: progress);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "blargh",
            NormalizedTranscript = "blargh"
        });

        Assert.Equal("knowledge_search", decision.IntentName);
        Assert.Equal(1, progress.PublishCount);
        Assert.Equal(1, knowledgeSearch.PublishCountSeenAtSearchStart);
        Assert.Contains("Thinking_01", progress.LastPayload, StringComparison.Ordinal);
    }

    private static JiboInteractionService CreateService(
        IWikipediaSummaryProvider? wikipediaSummaryProvider = null,
        IKnowledgeSearchService? knowledgeSearchService = null,
        ITurnProgressPublisher? turnProgressPublisher = null)
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore(),
            wikipediaSummaryProvider: wikipediaSummaryProvider,
            knowledgeSearchService: knowledgeSearchService,
            turnProgressPublisher: turnProgressPublisher);
    }

    private sealed class CapturingTurnProgressPublisher : ITurnProgressPublisher
    {
        public int PublishCount { get; private set; }
        public string LastPayload { get; private set; } = string.Empty;

        public Task PublishAsync(WebSocketReply reply, CancellationToken cancellationToken = default)
        {
            PublishCount += 1;
            LastPayload = reply.Text ?? string.Empty;
            return Task.CompletedTask;
        }

        public Task PublishSearchThinkingAsync(CancellationToken cancellationToken = default)
        {
            return PublishAsync(
                new WebSocketReply { Text = SearchThinkingSkillActionFactory.CreateThinkingJson("trans-thinking") },
                cancellationToken);
        }
    }

    private sealed class OrderingWikipediaSummaryProvider(
        string? summary,
        Func<int> onLookup) : IWikipediaSummaryProvider
    {
        public int PublishCountSeenAtLookupStart { get; private set; }

        public Task<string?> GetSummaryAsync(string subject, CancellationToken cancellationToken = default)
        {
            PublishCountSeenAtLookupStart = onLookup();
            return Task.FromResult(summary);
        }
    }

    private sealed class OrderingKnowledgeSearchService(
        KnowledgeSearchResult? result,
        Func<int> onSearch) : IKnowledgeSearchService
    {
        public bool IsConfigured => true;

        public int PublishCountSeenAtSearchStart { get; private set; }

        public Task<KnowledgeSearchResult?> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            PublishCountSeenAtSearchStart = onSearch();
            return Task.FromResult(result);
        }
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
