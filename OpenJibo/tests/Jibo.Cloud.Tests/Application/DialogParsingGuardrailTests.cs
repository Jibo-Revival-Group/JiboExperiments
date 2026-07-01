using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class DialogParsingGuardrailTests
{
    [Theory]
    [InlineData("can you dance", "robot_can_dance")]
    [InlineData("will you dance", "robot_can_dance")]
    [InlineData("are you good at dancing", "robot_can_dance")]
    [InlineData("can you do a dance", "robot_can_dance")]
    [InlineData("do you like to dance", "dance_question")]
    [InlineData("what is your favorite dance", "dance_question")]
    [InlineData("which dance do you like", "dance_question")]
    [InlineData("dance", "dance")]
    [InlineData("please dance", "dance")]
    [InlineData("show me your dance", "dance")]
    [InlineData("bust a move", "dance")]
    [InlineData("boogie", "dance")]
    [InlineData("tell me about dancing", "chat")]
    public async Task BuildDecisionAsync_DancePhrases_PreserveQuestionCommandSplit(
        string transcript,
        string expectedIntent)
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal(expectedIntent, decision.IntentName);
    }

    [Theory]
    [InlineData("twerk")]
    [InlineData("can you twerk")]
    [InlineData("show me a twerk")]
    public async Task BuildDecisionAsync_TwerkPhrases_PreserveSpecificDanceCommand(string transcript)
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("twerk", decision.IntentName);
    }


    [Theory]
    [InlineData("do you know my favorite color", "color")]
    [InlineData("tell me my favorite colour", "colour")]
    [InlineData("tell me what my favorite sport is", "sport")]
    [InlineData("tell me what my favourite food is", "food")]
    [InlineData("do you know what my favorite color is", "color")]
    [InlineData("do you remember what my favourite food is", "food")]
    [InlineData("what's my fave sport", "sport")]
    [InlineData("tell me my fave color", "color")]
    [InlineData("do you recall my favorite color", "color")]
    [InlineData("can you tell me my favourite food", "food")]
    [InlineData("do you recall what my favorite sport is", "sport")]
    [InlineData("can you tell me what my fave color is", "color")]
    [InlineData("can you please tell me my favorite color", "color")]
    [InlineData("could you please tell me my favourite food", "food")]
    [InlineData("would you please tell me what my fave sport is", "sport")]
    public async Task BuildDecisionAsync_PreferenceRecallAliases_StayOnMemoryRoute(
        string transcript,
        string expectedCategory)
    {
        var memoryStore = new InMemoryPersonalMemoryStore();
        memoryStore.SetPreference(
            new PersonalMemoryTenantScope("usr_openjibo_owner", "openjibo-default-loop", "Ghost-Instance-Onion-Silk"),
            expectedCategory,
            "blue");
        var service = CreateService(memoryStore);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("memory_get_preference", decision.IntentName);
        Assert.Contains($"favorite {expectedCategory}", decision.ReplyText);
    }

    private static JiboInteractionService CreateService(InMemoryPersonalMemoryStore? memoryStore = null)
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            memoryStore ?? new InMemoryPersonalMemoryStore());
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items)
        {
            return items[0];
        }
    }
}