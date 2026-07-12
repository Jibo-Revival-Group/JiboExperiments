using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class MathInteractionServiceTests
{
    [Theory]
    [InlineData("what's 12 plus 8", "12 plus 8 equals 20")]
    [InlineData("what's 6 times 5", "6 times 5 equals 30")]
    [InlineData("hey jibo whats nine plus ten", "nine plus ten equals 19, but some might say it's 21")]
    [InlineData("what is twelve add eight", "twelve plus eight equals 20")]
    [InlineData("what's the square root of 9", "the square root of 9 equals 3")]
    [InlineData("what's the square root of sixteen", "the square root of sixteen equals 4")]
    [InlineData("what's 9 to the power of 3", "9 to the power of 3 equals 729")]
    public async Task BuildDecisionAsync_MathQuery_ReturnsExpectedReply(string transcript, string expectedReply)
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("math_query", decision.IntentName);
        Assert.Equal(expectedReply, decision.ReplyText);
    }

    [Theory]
    [InlineData("what's 9 plus 10")]
    [InlineData("what is 10 plus 9")]
    public async Task BuildDecisionAsync_NinePlusTen_IncludesEasterEgg(string transcript)
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("math_query", decision.IntentName);
        Assert.Contains("19", decision.ReplyText);
        Assert.Contains("but some might say it's 21", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_OtherAddition_DoesNotIncludeEasterEgg()
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "what's 8 plus 8",
            NormalizedTranscript = "what's 8 plus 8",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("math_query", decision.IntentName);
        Assert.Equal("8 plus 8 equals 16", decision.ReplyText);
        Assert.DoesNotContain("21", decision.ReplyText);
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
