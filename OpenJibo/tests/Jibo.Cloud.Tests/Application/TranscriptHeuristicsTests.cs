using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class TranscriptHeuristicsTests
{
    [Theory]
    [InlineData("What's the capital of France")]
    [InlineData("what is the capital of France")]
    [InlineData("Can you tell me about France")]
    [InlineData("look up the capital of France")]
    [InlineData("What is the capital of France?")]
    public void IsLikelyQuestion_PegasusQuestionForms_ReturnsTrue(string transcript)
    {
        Assert.True(TranscriptHeuristics.IsLikelyQuestion(transcript));
    }

    [Theory]
    [InlineData("Gling lang gone")]
    [InlineData("tell me a joke")]
    [InlineData("play word of a day")]
    [InlineData("hello there")]
    public void IsLikelyQuestion_NonQuestionSpeech_ReturnsFalse(string transcript)
    {
        Assert.False(TranscriptHeuristics.IsLikelyQuestion(transcript));
    }
}
