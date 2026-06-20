using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class HomeAssistantInteractionServiceTests
{
    [Theory]
    [InlineData("turn off the lights")]
    [InlineData("lights off")]
    [InlineData("turn the lights off")]
    public async Task BuildDecisionAsync_HaLightsOff_RecognizesIntent(string transcript)
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-ha-intent-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var integrationStore = new InMemoryUserIntegrationStore(snapshotStore);
        integrationStore.AddHomeAssistantLink(
            "BOJW-1000-0017-0820-0020",
            "Ghost-Instance-Onion-Silk",
            "ha-instance-1");

        var service = CreateService(integrationStore);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("ha_lights_off", decision.IntentName);
        Assert.Equal("Okay, turning off the lights.", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_HaLightsOff_ReturnsFallback_WhenNotLinked()
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-ha-intent-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var integrationStore = new InMemoryUserIntegrationStore(snapshotStore);
        var service = CreateService(integrationStore);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "turn off the lights",
            NormalizedTranscript = "turn off the lights",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("ha_lights_off", decision.IntentName);
        Assert.Equal("I don't have Home Assistant set up for my room yet.", decision.ReplyText);
    }

    private static JiboInteractionService CreateService(InMemoryUserIntegrationStore integrationStore)
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore(),
            userIntegrationStore: integrationStore);
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];
    }
}
