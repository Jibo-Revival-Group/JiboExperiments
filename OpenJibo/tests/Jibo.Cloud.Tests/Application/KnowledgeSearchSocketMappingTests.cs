using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class KnowledgeSearchSocketMappingTests
{
    [Fact]
    public void Map_KnowledgeSearch_LaunchesNimbusAnswerAndSpeaksWithoutEmbeddedThinkingAnim()
    {
        var plan = new ResponsePlan
        {
            IntentName = "knowledge_search",
            Actions =
            {
                new SpeakAction
                {
                    Sequence = 0,
                    Text = "According to wikipedia. James Abram Garfield was the 20th president.",
                    Voice = "griffin"
                },
                new InvokeNativeSkillAction
                {
                    Sequence = 1,
                    SkillName = "chitchat-skill",
                    Payload = new Dictionary<string, object?>
                    {
                        ["cloudSkill"] = SearchThinkingPreludeFactory.AnswerCloudSkill
                    }
                }
            }
        };
        var turn = new TurnContext
        {
            NormalizedTranscript = "who is james garfield",
            Attributes = new Dictionary<string, object?>
            {
                ["transID"] = "trans-knowledge",
                ["messageType"] = "LISTEN",
                ["listenRules"] = new[] { "launch" }
            }
        };

        var replies = ResponsePlanToSocketMessagesMapper.Map(plan, turn, new CloudSession(), emitSkillActions: true);

        var listen = replies
            .Select(reply => reply.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => JsonDocument.Parse(text!))
            .First(document => document.RootElement.GetProperty("type").GetString() == "LISTEN");
        var skillAction = replies
            .Select(reply => reply.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => JsonDocument.Parse(text!))
            .First(document => document.RootElement.GetProperty("type").GetString() == "SKILL_ACTION");

        var match = listen.RootElement.GetProperty("data").GetProperty("match");
        Assert.Equal(SearchThinkingPreludeFactory.NimbusSkillId, match.GetProperty("skillID").GetString());
        Assert.Equal(SearchThinkingPreludeFactory.AnswerCloudSkill, match.GetProperty("cloudSkill").GetString());
        Assert.False(match.GetProperty("onRobot").GetBoolean());

        var esml = skillAction.RootElement
            .GetProperty("data")
            .GetProperty("action")
            .GetProperty("config")
            .GetProperty("jcp")
            .GetProperty("config")
            .GetProperty("play")
            .GetProperty("esml")
            .GetString();

        Assert.DoesNotContain("Thinking_Eye_Loop_01", esml, StringComparison.Ordinal);
        Assert.Contains("According to wikipedia.", esml, StringComparison.Ordinal);
        Assert.True(skillAction.RootElement.GetProperty("data").GetProperty("final").GetBoolean());
    }

    [Fact]
    public void Map_KnowledgeSearch_AfterPrelude_SkipsDuplicateListenAndEos()
    {
        var plan = new ResponsePlan
        {
            IntentName = "knowledge_search",
            Actions =
            {
                new SpeakAction
                {
                    Sequence = 0,
                    Text = "According to wikipedia. James Abram Garfield was the 20th president.",
                    Voice = "griffin"
                },
                new InvokeNativeSkillAction
                {
                    Sequence = 1,
                    SkillName = "chitchat-skill",
                    Payload = new Dictionary<string, object?>
                    {
                        ["cloudSkill"] = SearchThinkingPreludeFactory.AnswerCloudSkill
                    }
                }
            }
        };
        var session = new CloudSession();
        session.Metadata[SearchThinkingPreludeFactory.PreludeMetadataKey] = "trans-knowledge";
        var turn = new TurnContext
        {
            NormalizedTranscript = "who is james garfield",
            Attributes = new Dictionary<string, object?>
            {
                ["transID"] = "trans-knowledge",
                ["messageType"] = "LISTEN",
                ["listenRules"] = new[] { "launch" }
            }
        };

        var replies = ResponsePlanToSocketMessagesMapper.Map(plan, turn, session, emitSkillActions: true);
        var types = replies
            .Select(reply => reply.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => JsonDocument.Parse(text!).RootElement.GetProperty("type").GetString())
            .ToArray();

        Assert.DoesNotContain("LISTEN", types);
        Assert.DoesNotContain("EOS", types);
        Assert.Contains("SKILL_ACTION", types);
    }

    [Fact]
    public void Map_KnowledgeSearchNotFound_ShowsHeardTranscriptOnListen()
    {
        var plan = new ResponsePlan
        {
            IntentName = "knowledge_search_not_found",
            Actions =
            {
                new SpeakAction
                {
                    Sequence = 0,
                    Text = "I can't find anything.",
                    Voice = "griffin"
                },
                new InvokeNativeSkillAction
                {
                    Sequence = 1,
                    SkillName = "chitchat-skill",
                    Payload = new Dictionary<string, object?>
                    {
                        ["cloudSkill"] = SearchThinkingPreludeFactory.AnswerCloudSkill
                    }
                }
            }
        };
        var turn = new TurnContext
        {
            NormalizedTranscript = "who is zzxxyyqq",
            Attributes = new Dictionary<string, object?>
            {
                ["transID"] = "trans-not-found",
                ["messageType"] = "LISTEN",
                ["listenRules"] = new[] { "launch" }
            }
        };

        var replies = ResponsePlanToSocketMessagesMapper.Map(plan, turn, new CloudSession(), emitSkillActions: true);

        var listen = replies
            .Select(reply => reply.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => JsonDocument.Parse(text!))
            .First(document => document.RootElement.GetProperty("type").GetString() == "LISTEN");
        var skillAction = replies
            .Select(reply => reply.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => JsonDocument.Parse(text!))
            .First(document => document.RootElement.GetProperty("type").GetString() == "SKILL_ACTION");

        Assert.Equal(
            "who is zzxxyyqq",
            listen.RootElement.GetProperty("data").GetProperty("asr").GetProperty("text").GetString());
        Assert.Equal(
            SearchThinkingPreludeFactory.AnswerCloudSkill,
            listen.RootElement.GetProperty("data").GetProperty("match").GetProperty("cloudSkill").GetString());
        Assert.Contains(
            "I can't find anything.",
            skillAction.RootElement
                .GetProperty("data")
                .GetProperty("action")
                .GetProperty("config")
                .GetProperty("jcp")
                .GetProperty("config")
                .GetProperty("play")
                .GetProperty("esml")
                .GetString(),
            StringComparison.Ordinal);
    }
}

public sealed class SearchThinkingPreludeFactoryTests
{
    [Fact]
    public void CreateListenAndEos_UsesNimbusAnswerMatch()
    {
        var replies = SearchThinkingPreludeFactory.CreateListenAndEos(
            "trans-123",
            "who is james garfield",
            ["launch"]);

        Assert.Equal(2, replies.Count);
        using var listen = JsonDocument.Parse(replies[0].Text!);
        using var eos = JsonDocument.Parse(replies[1].Text!);
        Assert.Equal("LISTEN", listen.RootElement.GetProperty("type").GetString());
        Assert.Equal("EOS", eos.RootElement.GetProperty("type").GetString());

        var match = listen.RootElement.GetProperty("data").GetProperty("match");
        Assert.Equal(SearchThinkingPreludeFactory.NimbusSkillId, match.GetProperty("skillID").GetString());
        Assert.Equal(SearchThinkingPreludeFactory.AnswerCloudSkill, match.GetProperty("cloudSkill").GetString());
        Assert.False(match.GetProperty("onRobot").GetBoolean());
    }
}

public sealed class AmbientTurnProgressPublisherTests
{
    [Fact]
    public async Task PublishSearchThinkingPreludeAsync_SendsListenAndEosOnce()
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
            await publisher.PublishSearchThinkingPreludeAsync();
            await publisher.PublishSearchThinkingPreludeAsync();
        }

        Assert.Equal(2, sent.Count);
        Assert.Contains("LISTEN", sent[0], StringComparison.Ordinal);
        Assert.Contains("EOS", sent[1], StringComparison.Ordinal);
        Assert.DoesNotContain("SKILL_ACTION", string.Join('\n', sent), StringComparison.Ordinal);
        Assert.Equal(
            "trans-abc",
            session.Metadata[SearchThinkingPreludeFactory.PreludeMetadataKey]?.ToString());
    }
}

public sealed class SearchThinkingOrderingTests
{
    [Fact]
    public async Task BuildDecisionAsync_PublishesPreludeBeforeWikipediaLookup()
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
        Assert.Equal("answer", decision.SkillPayload?["cloudSkill"]?.ToString());
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

        public Task PublishSearchThinkingPreludeAsync(CancellationToken cancellationToken = default)
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

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];
    }
}
