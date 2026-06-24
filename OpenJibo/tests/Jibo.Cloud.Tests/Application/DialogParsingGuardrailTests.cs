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
    [InlineData("do you like to dance", "dance_question")]
    [InlineData("dance", "dance")]
    [InlineData("please dance", "dance")]
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

    private static JiboInteractionService CreateService()
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore());
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];
    }
}
