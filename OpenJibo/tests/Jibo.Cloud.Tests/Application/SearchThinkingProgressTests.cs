using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Tests.Application;

public sealed class SearchThinkingSkillActionFactoryTests
{
    [Fact]
    public void CreateJson_IncludesEyeThinkingAnimationAndNonFinalFlag()
    {
        var json = SearchThinkingSkillActionFactory.CreateJson("trans-123");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("SKILL_ACTION", root.GetProperty("type").GetString());
        Assert.Equal("trans-123", root.GetProperty("transID").GetString());
        Assert.False(root.GetProperty("data").GetProperty("final").GetBoolean());

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
        Assert.Contains("eye_thinking_01", esml, StringComparison.Ordinal);
    }
}

public sealed class AmbientTurnProgressPublisherTests
{
    [Fact]
    public async Task PublishSearchThinkingAsync_SendsWhenScopeIsActive()
    {
        WebSocketReply? sent = null;
        var publisher = new AmbientTurnProgressPublisher();

        using (AmbientTurnProgressPublisher.Begin(
                   () => "trans-abc",
                   (reply, _) =>
                   {
                       sent = reply;
                       return Task.CompletedTask;
                   }))
        {
            await publisher.PublishSearchThinkingAsync();
        }

        Assert.NotNull(sent);
        Assert.Contains("eye_thinking_01", sent!.Text, StringComparison.Ordinal);
        Assert.Contains("trans-abc", sent.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishSearchThinkingAsync_IsNoOpWithoutScope()
    {
        var publisher = new AmbientTurnProgressPublisher();
        await publisher.PublishSearchThinkingAsync();
    }
}
