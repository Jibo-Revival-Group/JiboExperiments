using Jibo.Cloud.Application.Services;

// ReSharper disable StringLiteralTypo

namespace Jibo.Cloud.Tests.WebSockets;

public sealed class AudioTranscriptNormalizerTests
{
    [Theory]
    [InlineData("Jupo. What's your cloud version?", "what's your cloud version")]
    [InlineData("Jubo. What's your cloud version?", "what's your cloud version")]
    [InlineData("Hey GBO, what's the word of the day?", "what's the word of the day")]
    [InlineData("Hey G-Bell, what's your cloud version?", "what's your cloud version")]
    [InlineData("Hey G Bong stop", "stop")]
    [InlineData("Hey Jibo stop", "stop")]
    [InlineData("Hey Jibo, how ya doing?", "how ya doing")]
    [InlineData("Hey j bowl, what's your cloud version?", "what's your cloud version")]
    [InlineData("jibo what time is it", "what time is it")]
    public void StripLeadingWakePhrase_RemovesKnownWakePhraseVariants(string value, string expected)
    {
        Assert.Equal(expected, TranscriptTextNormalizer.StripLeadingWakePhrase(value));
    }

    [Theory]
    [InlineData("dot version 1 dot 0 dot 20. Hey Jibo, what time is it?", "what time is it")]
    [InlineData("one dot zero dot twenty. Hey Jibo, what time is it?", "what time is it")]
    [InlineData("first 1.0.20. Hey Geebo, what time is", "what time is")]
    [InlineData("story. Hey GBO, what's the word of the day?", "what's the word of the day")]
    [InlineData("story. Hey G-Bell, what's your cloud version?", "what's your cloud version")]
    [InlineData("that fixed foot. Hey j bowl, does James have a floopy diaper?", "does james have a floopy diaper")]
    [InlineData("it's the definition again long out order is complete hey g bong stop", "stop")]
    [InlineData("Jibo. What's your cloud version?", "what's your cloud version")]
    public void ExtractWakePhraseCommand_RemovesLeadingOrEmbeddedWakePhrase(string value, string expected)
    {
        Assert.Equal(expected, TranscriptTextNormalizer.ExtractWakePhraseCommand(value));
    }

    [Theory]
    [InlineData("Hey, Jibo.")]
    [InlineData("Jupo.")]
    [InlineData("Hey, G-Bell.")]
    [InlineData("Hey, Jim.")]
    [InlineData("Hey j bowl.")]
    [InlineData("hello gebo")]
    public void IsWakePhraseOnly_ReturnsTrue_ForWakePhraseOnlyTranscripts(string value)
    {
        Assert.True(TranscriptTextNormalizer.IsWakePhraseOnly(value));
    }

    [Theory]
    [InlineData("version 1 dot 0 dot 20 hey gebo")]
    [InlineData("the snail said whee hey jibo")]
    [InlineData("at all hey gibo")]
    public void HasTerminalWakePhraseWithoutCommand_ReturnsTrue_WhenPriorAudioEndsWithWakePhrase(string value)
    {
        Assert.True(TranscriptTextNormalizer.HasTerminalWakePhraseWithoutCommand(value));
    }

    [Theory]
    [InlineData("hey jibo")]
    [InlineData("hey jibo what's your cloud version")]
    [InlineData("okay you said feeling thankful hey jibo what's your cloud version")]
    public void HasTerminalWakePhraseWithoutCommand_ReturnsFalse_WhenWakePhraseHasNoPriorAudioOrHasCommand(string value)
    {
        Assert.False(TranscriptTextNormalizer.HasTerminalWakePhraseWithoutCommand(value));
    }

    [Theory]
    [InlineData("I heard you.")]
    [InlineData("Okay, you said.")]
    [InlineData("I can hear you")]
    [InlineData("I didn't catch that")]
    [InlineData("Say that again")]
    [InlineData("Thanks for watching")]
    [InlineData("you said")]
    [InlineData("I hope you try again in a little while.")]
    public void IsLikelyRobotSelfAudioTranscript_ReturnsTrue_ForRobotAcknowledgements(string value)
    {
        Assert.True(TranscriptHeuristics.IsLikelyRobotSelfAudioTranscript(value));
    }

    [Theory]
    [InlineData("Do you want to hear something")]
    [InlineData("Would you like to play the word of the day game")]
    [InlineData("Can we play the same word")]
    [InlineData("I heard you")]
    [InlineData("Do you want to take a picture")]
    [InlineData("Do you want to do yoga now")]
    [InlineData("Want to take a picture now")]
    [InlineData("Oh there are no photos yet. Do you want to take one now")]
    public void IsLikelyPromptEchoTranscript_ReturnsTrue_ForPromptEchoOrRobotSelfAudio(string value)
    {
        Assert.True(TranscriptHeuristics.IsLikelyPromptEchoTranscript(value));
    }

    [Theory]
    [InlineData("cloud version")]
    [InlineData("what's your cloud version")]
    [InlineData("hey jibo stop")]
    public void IsLikelyRobotSelfAudioTranscript_ReturnsFalse_ForUserSpeech(string value)
    {
        Assert.False(TranscriptHeuristics.IsLikelyRobotSelfAudioTranscript(value));
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no yes")]
    [InlineData("I want to do yoga")]
    public void IsLikelyPromptEchoTranscript_ReturnsFalse_ForUserSpeech(string value)
    {
        Assert.False(TranscriptHeuristics.IsLikelyPromptEchoTranscript(value));
    }

    [Theory]
    [InlineData("Do you want to take a picture")]
    [InlineData("Do you want to do yoga now")]
    [InlineData("Want to take a picture now")]
    [InlineData("Oh there are no photos yet. Do you want to take one now")]
    public void IsLikelySkillOfferPromptEcho_ReturnsTrue_ForGalleryAndYogaOffers(string value)
    {
        Assert.True(TranscriptHeuristics.IsLikelySkillOfferPromptEcho(value));
    }

    [Theory]
    [InlineData("what do you want to talk about")]
    [InlineData("what would you like to talk about")]
    [InlineData("I want to do yoga")]
    [InlineData("take a picture")]
    public void IsLikelySkillOfferPromptEcho_ReturnsFalse_ForPersonalityAndCommands(string value)
    {
        Assert.False(TranscriptHeuristics.IsLikelySkillOfferPromptEcho(value));
    }
}
