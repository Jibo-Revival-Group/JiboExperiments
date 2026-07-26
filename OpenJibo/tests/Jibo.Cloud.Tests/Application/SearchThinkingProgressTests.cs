using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class SearchThinkingSkillActionFactoryTests
{
    [Fact]
    public void CreateThinkingJson_IncludesThinkingAnimationFireAndForgetAndNonFinalFlag()
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
        Assert.Contains("Thinking_01", esml, StringComparison.Ordinal);
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
        Assert.Contains("Thinking_01", sent[2], StringComparison.Ordinal);
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
        Assert.Contains("Thinking_01", sent[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishSearchThinkingAsync_IsNoOpWithoutScope()
    {
        var publisher = new AmbientTurnProgressPublisher();
        await publisher.PublishSearchThinkingAsync();
    }
}
