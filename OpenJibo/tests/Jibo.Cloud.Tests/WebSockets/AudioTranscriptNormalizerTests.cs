using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Audio;

namespace Jibo.Cloud.Tests.WebSockets;

public sealed class AudioTranscriptNormalizerTests
{
    [Theory]
    [InlineData("Jupo. What's your cloud version?", "what's your cloud version")]
    [InlineData("Hey Jibo stop", "stop")]
    [InlineData("jibo what time is it", "what time is it")]
    public void StripLeadingWakePhrase_RemovesKnownWakePhraseVariants(string value, string expected)
    {
        Assert.Equal(expected, TranscriptTextNormalizer.StripLeadingWakePhrase(value));
    }

    [Theory]
    [InlineData("Hey, Jibo.")]
    [InlineData("Jupo.")]
    [InlineData("hello gebo")]
    public void IsWakePhraseOnly_ReturnsTrue_ForWakePhraseOnlyTranscripts(string value)
    {
        Assert.True(TranscriptTextNormalizer.IsWakePhraseOnly(value));
    }

    [Theory]
    [InlineData("I heard you.")]
    [InlineData("Okay, you said.")]
    [InlineData("I can hear you")]
    [InlineData("you said")]
    public void IsLikelyRobotSelfAudioTranscript_ReturnsTrue_ForRobotAcknowledgements(string value)
    {
        Assert.True(TranscriptHeuristics.IsLikelyRobotSelfAudioTranscript(value));
    }

    [Theory]
    [InlineData("cloud version")]
    [InlineData("what's your cloud version")]
    [InlineData("hey jibo stop")]
    public void IsLikelyRobotSelfAudioTranscript_ReturnsFalse_ForUserSpeech(string value)
    {
        Assert.False(TranscriptHeuristics.IsLikelyRobotSelfAudioTranscript(value));
    }
}
