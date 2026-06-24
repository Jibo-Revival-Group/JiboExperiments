using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class HomeAssistantLightCommandParserTests
{
    [Theory]
    [InlineData("turn off the lights", HomeAssistantLightCommandParser.LightAction.Off, HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("lights off", HomeAssistantLightCommandParser.LightAction.Off, HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("turn on the lights", HomeAssistantLightCommandParser.LightAction.On, HomeAssistantLightCommandParser.LightScope.Room)]
    [InlineData("lights on", HomeAssistantLightCommandParser.LightAction.On, HomeAssistantLightCommandParser.LightScope.Room)]
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
