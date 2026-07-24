using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class HomeAssistantLightCommandParserTests
{
    [Theory]
    [InlineData("turn off the lights", HomeAssistantLightCommandParser.LightAction.Off,
        HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("lights off", HomeAssistantLightCommandParser.LightAction.Off,
        HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("turn on the lights", HomeAssistantLightCommandParser.LightAction.On,
        HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("lights on", HomeAssistantLightCommandParser.LightAction.On,
        HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("kill the lights", HomeAssistantLightCommandParser.LightAction.Off,
        HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("shut off the lights", HomeAssistantLightCommandParser.LightAction.Off,
        HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("shut the lights off", HomeAssistantLightCommandParser.LightAction.Off,
        HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("turn all the lights off", HomeAssistantLightCommandParser.LightAction.Off,
        HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("all lights off", HomeAssistantLightCommandParser.LightAction.Off,
        HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("turn all the lights on", HomeAssistantLightCommandParser.LightAction.On,
        HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("all lights on", HomeAssistantLightCommandParser.LightAction.On,
        HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("turn off the lights please", HomeAssistantLightCommandParser.LightAction.Off,
        HomeAssistantLightCommandParser.LightScope.Room)]
    public void TryParse_RoomCommands_ReturnExpectedScope(
        string transcript,
        HomeAssistantLightCommandParser.LightAction expectedAction,
        HomeAssistantLightCommandParser.LightScope expectedScope)
    {
        var parsed = HomeAssistantLightCommandParser.TryParse(transcript, out var command);

        Assert.True(parsed);
        Assert.Equal(expectedAction, command.Action);
        Assert.Equal(expectedScope, command.Scope);
        Assert.Null(command.TargetName);
    }

    [Theory]
    [InlineData("turn off zanes light", "zanes")]
    [InlineData("turn off zane's light", "zane's")]
    [InlineData("switch on zanes lamp", "zanes")]
    [InlineData("turn on the bedroom light", "bedroom")]
    [InlineData("turn bedroom lights off", "bedroom")]
    [InlineData("turn the bedroom lights on", "bedroom")]
    [InlineData("lights off in bedroom", "bedroom")]
    [InlineData("lights on in living room", "living room")]
    [InlineData("turn lights off in bedroom", "bedroom")]
    public void TryParse_NamedCommands_ReturnTarget(string transcript, string expectedTarget)
    {
        var parsed = HomeAssistantLightCommandParser.TryParse(transcript, out var command);

        Assert.True(parsed);
        Assert.Equal(HomeAssistantLightCommandParser.LightScope.Named, command.Scope);
        Assert.Equal(expectedTarget, command.TargetName);
    }

    [Fact]
    public void FormatTargetForSpeech_AppendsLightWhenMissing()
    {
        Assert.Equal("zanes light", HomeAssistantLightCommandParser.FormatTargetForSpeech("zanes"));
        Assert.Equal("zane's light", HomeAssistantLightCommandParser.FormatTargetForSpeech("zane's light"));
    }
}
