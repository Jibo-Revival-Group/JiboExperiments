using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class SearchThinkingSkillActionFactoryTests
{
    [Fact]
    public void CreateThinkingJson_IncludesThinkingEyeLoopFireAndForgetAndNonFinalFlag()
    {
        var json = SearchThinkingSkillActionFactory.CreateThinkingJson("trans-123");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("SKILL_ACTION", root.GetProperty("type").GetString());
        Assert.Equal("trans-123", root.GetProperty("transID").GetString());
        Assert.False(root.GetProperty("data").GetProperty("final").GetBoolean());
        Assert.True(root.GetProperty("data").GetProperty("fireAndForget").GetBoolean());

        var esml = root
            .GetProperty("data")
            .GetProperty("action")
            .GetProperty("config")
            .GetProperty("jcp")
            .GetProperty("config")
            .GetProperty("play")
            .GetProperty("esml")
            .GetString();

        Assert.Equal(SearchThinkingSkillActionFactory.ThinkingAnimationEsml, esml);
        Assert.Contains("Thinking_Eye_Loop_01", esml, StringComparison.Ordinal);
        Assert.Contains("nonBlocking", esml, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateListenAndEos_EmitsListenThenEos()
    {
        var replies = SearchThinkingSkillActionFactory.CreateListenAndEos(
            "trans-123",
            "who is james garfield",
            ["launch"]);

        Assert.Equal(2, replies.Count);
        using var listen = JsonDocument.Parse(replies[0].Text!);
        using var eos = JsonDocument.Parse(replies[1].Text!);
        Assert.Equal("LISTEN", listen.RootElement.GetProperty("type").GetString());
        Assert.Equal("EOS", eos.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "who is james garfield",
            listen.RootElement.GetProperty("data").GetProperty("asr").GetProperty("text").GetString());
    }
}

public sealed class AmbientTurnProgressPublisherTests
{
    [Fact]
    public async Task PublishSearchThinkingAsync_SendsListenEosThenThinking()
    {
        var sent = new List<string>();
        var session = new CloudSession();
        var turn = new TurnContext
        {
            NormalizedTranscript = "who is james garfield",
            Attributes = new Dictionary<string, object?>
            {
                ["transID"] = "trans-abc",
                ["listenRules"] = new[] { "launch" }
            }
        };
        var publisher = new AmbientTurnProgressPublisher();

        using (AmbientTurnProgressPublisher.Begin((reply, _) =>
               {
                   sent.Add(reply.Text ?? string.Empty);
                   return Task.CompletedTask;
               }))
        {
            AmbientTurnProgressPublisher.BindTurn(turn, session);
            await publisher.PublishSearchThinkingAsync();
        }

        Assert.Equal(3, sent.Count);
        Assert.Contains("LISTEN", sent[0], StringComparison.Ordinal);
        Assert.Contains("EOS", sent[1], StringComparison.Ordinal);
        Assert.Contains("Thinking_Eye_Loop_01", sent[2], StringComparison.Ordinal);
        Assert.Contains("\"fireAndForget\":true", sent[2], StringComparison.Ordinal);
        Assert.Equal(
            "trans-abc",
            session.Metadata[SearchThinkingSkillActionFactory.PreludeMetadataKey]?.ToString());
    }

    [Fact]
    public async Task PublishSearchThinkingAsync_DoesNotResendListenWhenPreludeAlreadyMarked()
    {
        var sent = new List<string>();
        var session = new CloudSession();
        session.Metadata[SearchThinkingSkillActionFactory.PreludeMetadataKey] = "trans-abc";
        var turn = new TurnContext
        {
            NormalizedTranscript = "who is james garfield",
            Attributes = new Dictionary<string, object?> { ["transID"] = "trans-abc" }
        };
        var publisher = new AmbientTurnProgressPublisher();

        using (AmbientTurnProgressPublisher.Begin((reply, _) =>
               {
                   sent.Add(reply.Text ?? string.Empty);
                   return Task.CompletedTask;
               }))
        {
            AmbientTurnProgressPublisher.BindTurn(turn, session);
            await publisher.PublishSearchThinkingAsync();
        }

        Assert.Single(sent);
        Assert.Contains("Thinking_Eye_Loop_01", sent[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishSearchThinkingAsync_IsNoOpWithoutScope()
    {
        var publisher = new AmbientTurnProgressPublisher();
        await publisher.PublishSearchThinkingAsync();
    }
}

public sealed class SearchThinkingOrderingTests
{
    [Fact]
    public async Task BuildDecisionAsync_PublishesThinkingBeforeWikipediaLookup()
    {
        var previousGrace = SearchThinkingSkillActionFactory.AnimStartGrace;
        SearchThinkingSkillActionFactory.AnimStartGrace = TimeSpan.Zero;
        try
        {
            var progress = new CapturingTurnProgressPublisher();
            var wikipedia = new OrderingWikipediaSummaryProvider(
                WikipediaSummaryResult.Found(
                    "James Abram Garfield was the 20th president of the United States."),
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
            Assert.Equal(1, wikipedia.PublishCountSeenAtLookup);
            Assert.True(wikipedia.PublishCountSeenAtLookup <= progress.PublishCount);
        }
        finally
        {
            SearchThinkingSkillActionFactory.AnimStartGrace = previousGrace;
        }
    }

    [Fact]
    public async Task BuildDecisionAsync_PublishesThinkingBeforeKnowledgeSearch()
    {
        var previousGrace = SearchThinkingSkillActionFactory.AnimStartGrace;
        SearchThinkingSkillActionFactory.AnimStartGrace = TimeSpan.Zero;
        try
        {
            var progress = new CapturingTurnProgressPublisher();
            var knowledgeSearch = new OrderingKnowledgeSearchService(
                new KnowledgeSearchResult("Answer.", SearchBackendKind.Wolfram),
                onSearch: () => progress.PublishCount);
            var service = CreateService(
                knowledgeSearchService: knowledgeSearch,
                turnProgressPublisher: progress);

            var decision = await service.BuildDecisionAsync(new TurnContext
            {
                RawTranscript = "What is the 20th president",
                NormalizedTranscript = "What is the 20th president"
            });

            Assert.Equal("knowledge_search", decision.IntentName);
            Assert.Equal(1, progress.PublishCount);
            Assert.Equal(1, knowledgeSearch.PublishCountSeenAtSearch);
        }
        finally
        {
            SearchThinkingSkillActionFactory.AnimStartGrace = previousGrace;
        }
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

        public Task PublishAsync(WebSocketReply reply, CancellationToken cancellationToken = default)
        {
            PublishCount += 1;
            return Task.CompletedTask;
        }

        public Task PublishSearchThinkingAsync(CancellationToken cancellationToken = default)
        {
            PublishCount += 1;
            return Task.CompletedTask;
        }
    }

    private sealed class OrderingWikipediaSummaryProvider(
        WikipediaSummaryResult result,
        Func<int> onLookup) : IWikipediaSummaryProvider
    {
        public int PublishCountSeenAtLookup { get; private set; }

        public Task<WikipediaSummaryResult> GetSummaryAsync(
            string subject,
            CancellationToken cancellationToken = default,
            bool bypassCache = false)
        {
            PublishCountSeenAtLookup = onLookup();
            return Task.FromResult(result);
        }
    }

    private sealed class OrderingKnowledgeSearchService(
        KnowledgeSearchResult result,
        Func<int> onSearch) : IKnowledgeSearchService
    {
        public bool IsConfigured => true;

        public int PublishCountSeenAtSearch { get; private set; }

        public Task<KnowledgeSearchResult?> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            PublishCountSeenAtSearch = onSearch();
            return Task.FromResult<KnowledgeSearchResult?>(result);
        }
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];
    }
}
