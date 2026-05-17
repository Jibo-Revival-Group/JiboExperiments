using Jibo.Cloud.Application.Services;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.WebSockets;

public sealed class SyntheticBufferedAudioSttStrategyTests
{
    [Fact]
    public async Task TranscribeAsync_NormalizesLoosePunctuationInTranscriptHint()
    {
        var strategy = new SyntheticBufferedAudioSttStrategy();
        var result = await strategy.TranscribeAsync(new TurnContext
        {
            Attributes = new Dictionary<string, object?>
            {
                ["bufferedAudioBytes"] = 42,
                ["audioTranscriptHint"] = "- Thank you. - Yes."
            }
        });

        Assert.Equal("thank you yes", result.Text);
        Assert.Equal("synthetic-buffered-audio", result.Provider);
        Assert.Equal(0.75f, result.Confidence);
    }
}
