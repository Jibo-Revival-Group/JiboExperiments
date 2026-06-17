using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Audio;

namespace Jibo.Cloud.Tests.WebSockets;

public sealed class AudioTranscriptNormalizerTests
{
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
