using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class HomeAssistantInteractionServiceTests
{
    [Theory]
    [InlineData("turn off the lights", "ha_lights_off", "Okay, turning off the lights.")]
    [InlineData("lights off", "ha_lights_off", "Okay, turning off the lights.")]
    [InlineData("turn the lights off", "ha_lights_off", "Okay, turning off the lights.")]
    [InlineData("turn on the lights", "ha_lights_on", "Okay, turning on the lights.")]
    [InlineData("lights on", "ha_lights_on", "Okay, turning on the lights.")]
    [InlineData("turn off zanes light", "ha_lights_off", "Okay, turning off zanes light.")]
    [InlineData("turn on zane's light", "ha_lights_on", "Okay, turning on zane's light.")]
    [InlineData("kill the lights", "ha_lights_off", "Okay, turning off the lights.")]
    [InlineData("lights off in bedroom", "ha_lights_off", "Okay, turning off bedroom light.")]
    [InlineData("set the temperature to 69", "ha_climate_set_temp", "Okay, setting the temperature to 69 degrees.")]
    [InlineData("make it 72", "ha_climate_set_temp", "Okay, setting the temperature to 72 degrees.")]
    [InlineData("set the bedroom thermostat to 72", "ha_climate_set_temp",
        "Okay, setting the bedroom thermostat to 72 degrees.")]
    [InlineData("it's hot in here", "ha_climate_cool_down", "Okay, I'll cool things down a bit.")]
    [InlineData("it's cold in here", "ha_climate_warm_up", "Okay, I'll warm things up a bit.")]
    [InlineData("what temperature is it in here", "ha_climate_get_temp", "Okay, I'll check the temperature.")]
    [InlineData("what's the bedroom temperature", "ha_climate_get_temp", "Okay, I'll check the bedroom thermostat.")]
    public async Task BuildDecisionAsync_HaLights_RecognizesIntent(
        string transcript,
        string expectedIntent,
        string expectedReply)
    {
        var integrationStore = CreateLinkedIntegrationStore();
        var cloudStateStore = CreateCloudStateStore();
        var service = CreateService(integrationStore, cloudStateStore);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal(expectedIntent, decision.IntentName);
        Assert.Equal(expectedReply, decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_IndoorTemperature_BeatsRequestWeatherClientIntent()
    {
        var integrationStore = CreateLinkedIntegrationStore();
        var cloudStateStore = CreateCloudStateStore();
        var service = CreateService(integrationStore, cloudStateStore);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "whats the temperature in here",
            NormalizedTranscript = "whats the temperature in here",
            DeviceId = "Ghost-Instance-Onion-Silk",
            Attributes = new Dictionary<string, object?>
            {
                ["clientIntent"] = "requestWeather"
            }
        });

        Assert.Equal("ha_climate_get_temp", decision.IntentName);
        Assert.Equal("Okay, I'll check the temperature.", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_BareTemperature_KeepsWeather_WhenRequestWeatherClientIntent()
    {
        var integrationStore = CreateLinkedIntegrationStore();
        var cloudStateStore = CreateCloudStateStore();
        var service = CreateService(integrationStore, cloudStateStore);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "whats the temperature",
            NormalizedTranscript = "whats the temperature",
            DeviceId = "Ghost-Instance-Onion-Silk",
            Attributes = new Dictionary<string, object?>
            {
                ["clientIntent"] = "requestWeather"
            }
        });

        Assert.Equal("weather", decision.IntentName);
    }

    [Fact]
    public async Task BuildDecisionAsync_HaLightsOff_ReturnsFallback_WhenNotLinked()
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-ha-intent-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var integrationStore = new InMemoryUserIntegrationStore(snapshotStore);
        var service = CreateService(integrationStore, CreateCloudStateStore());

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "turn off the lights",
            NormalizedTranscript = "turn off the lights",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("ha_lights_off", decision.IntentName);
        Assert.Equal("I don't have Home Assistant set up for my room yet.", decision.ReplyText);
    }

    [Theory]
    [InlineData("verify me")]
    [InlineData("what's my verification code")]
    [InlineData("whats my verification code")]
    [InlineData("what is my verification code")]
    [InlineData("very fry me")]
    [InlineData("terrify me")]
    public async Task BuildDecisionAsync_VerifyMe_RecognizesPhrases(string transcript)
    {
        var service = new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore(),
            jiboVerificationService: new JiboVerificationService());

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("verify_me", decision.IntentName);
        Assert.Matches(
            @"^Your verification code is (?:zero|one|two|three|four|five|six|seven|eight|nine)(?: (?:zero|one|two|three|four|five|six|seven|eight|nine)){3}\.$",
            decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_VerifyMe_SpeaksDigitsAsWords()
    {
        var service = new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore(),
            jiboVerificationService: new JiboVerificationService());

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "verify me",
            NormalizedTranscript = "verify me",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("verify_me", decision.IntentName);
        Assert.Matches(
            @"^Your verification code is (?:zero|one|two|three|four|five|six|seven|eight|nine)(?: (?:zero|one|two|three|four|five|six|seven|eight|nine)){3}\.$",
            decision.ReplyText);
    }

    private static InMemoryUserIntegrationStore CreateLinkedIntegrationStore()
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-ha-intent-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var integrationStore = new InMemoryUserIntegrationStore(snapshotStore);
        integrationStore.AddHomeAssistantLink(
            "BOJW-1000-0017-0820-0020",
            "Ghost-Instance-Onion-Silk",
            "ha-instance-1");
        return integrationStore;
    }

    private static InMemoryCloudStateStore CreateCloudStateStore()
    {
        var cloudStateStore = new InMemoryCloudStateStore();
        cloudStateStore.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = "Test Robot"
        });

        return cloudStateStore;
    }

    private static JiboInteractionService CreateService(
        InMemoryUserIntegrationStore integrationStore,
        InMemoryCloudStateStore cloudStateStore)
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore(),
            cloudStateStore: cloudStateStore,
            userIntegrationStore: integrationStore);
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items)
        {
            return items[0];
        }
    }
}