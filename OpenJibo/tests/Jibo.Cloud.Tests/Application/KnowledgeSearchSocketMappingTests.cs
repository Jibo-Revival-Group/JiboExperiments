using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class KnowledgeSearchSocketMappingTests
{
    [Fact]
    public void Map_KnowledgeSearch_IncludesThinkingEyeLoopAnimation()
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

        var skillAction = replies
            .Select(reply => reply.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => JsonDocument.Parse(text!))
            .First(document => document.RootElement.GetProperty("type").GetString() == "SKILL_ACTION");

        var esml = skillAction.RootElement
            .GetProperty("data")
            .GetProperty("action")
            .GetProperty("config")
            .GetProperty("jcp")
            .GetProperty("config")
            .GetProperty("play")
            .GetProperty("esml")
            .GetString();

        Assert.Contains("Thinking_Eye_Loop_01", esml, StringComparison.Ordinal);
        Assert.DoesNotContain("nonBlocking", esml, StringComparison.Ordinal);
        Assert.Contains("According to wikipedia.", esml, StringComparison.Ordinal);
        Assert.True(skillAction.RootElement.GetProperty("data").GetProperty("final").GetBoolean());
    }

    [Fact]
    public void Map_KnowledgeSearchNotFound_ShowsHeardTranscriptOnListenAndSaysCantFind()
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

        var esml = skillAction.RootElement
            .GetProperty("data")
            .GetProperty("action")
            .GetProperty("config")
            .GetProperty("jcp")
            .GetProperty("config")
            .GetProperty("play")
            .GetProperty("esml")
            .GetString();

        Assert.Contains("I can't find anything.", esml, StringComparison.Ordinal);
        Assert.Contains("Thinking_Eye_Loop_01", esml, StringComparison.Ordinal);
        Assert.DoesNotContain("nonBlocking", esml, StringComparison.Ordinal);
    }
}
