using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class HomeAssistantClimateCommandParserTests
{
    [Theory]
    [InlineData("set the temperature to 69", HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
        HomeAssistantClimateCommandParser.ClimateScope.Room, null, 69)]
    [InlineData("set temperature to 72 degrees", HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
        HomeAssistantClimateCommandParser.ClimateScope.Room, null, 72)]
    [InlineData("set thermostat to 68", HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
        HomeAssistantClimateCommandParser.ClimateScope.Room, null, 68)]
    [InlineData("change the temp to 70", HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
        HomeAssistantClimateCommandParser.ClimateScope.Room, null, 70)]
    [InlineData("make it 72", HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
        HomeAssistantClimateCommandParser.ClimateScope.Room, null, 72)]
    [InlineData("make it to 69 degrees", HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
        HomeAssistantClimateCommandParser.ClimateScope.Room, null, 69)]
    [InlineData("make the temperature 70", HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
        HomeAssistantClimateCommandParser.ClimateScope.Room, null, 70)]
    [InlineData("make thermostat to 68", HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
        HomeAssistantClimateCommandParser.ClimateScope.Room, null, 68)]
    [InlineData("temperature to 72", HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
        HomeAssistantClimateCommandParser.ClimateScope.Room, null, 72)]
    [InlineData("temp to 65", HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
        HomeAssistantClimateCommandParser.ClimateScope.Room, null, 65)]
    [InlineData("thermostat to 71 degrees", HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
        HomeAssistantClimateCommandParser.ClimateScope.Room, null, 71)]
    public void TryParse_SetTemperatureRoomCommands_ReturnExpected(
        string transcript,
        HomeAssistantClimateCommandParser.ClimateAction expectedAction,
        HomeAssistantClimateCommandParser.ClimateScope expectedScope,
        string? expectedTarget,
        decimal expectedTemperature)
    {
        var parsed = HomeAssistantClimateCommandParser.TryParse(transcript, out var command);

        Assert.True(parsed);
        Assert.Equal(expectedAction, command.Action);
        Assert.Equal(expectedScope, command.Scope);
        Assert.Equal(expectedTarget, command.TargetName);
        Assert.Equal(expectedTemperature, command.Temperature);
    }

    [Theory]
    [InlineData("set the bedroom thermostat to 72", "bedroom", 72)]
    [InlineData("set temperature in living room to 68", "living room", 68)]
    [InlineData("adjust the upstairs thermostat to 75", "upstairs", 75)]
    public void TryParse_SetTemperatureNamedCommands_ReturnTarget(
        string transcript,
        string expectedTarget,
        decimal expectedTemperature)
    {
        var parsed = HomeAssistantClimateCommandParser.TryParse(transcript, out var command);

        Assert.True(parsed);
        Assert.Equal(HomeAssistantClimateCommandParser.ClimateScope.Named, command.Scope);
        Assert.Equal(expectedTarget, command.TargetName);
        Assert.Equal(expectedTemperature, command.Temperature);
    }

    [Theory]
    [InlineData("it's hot in here", HomeAssistantClimateCommandParser.ClimateAction.CoolDown)]
    [InlineData("its hot in here", HomeAssistantClimateCommandParser.ClimateAction.CoolDown)]
    [InlineData("too hot", HomeAssistantClimateCommandParser.ClimateAction.CoolDown)]
    [InlineData("make it cooler", HomeAssistantClimateCommandParser.ClimateAction.CoolDown)]
    [InlineData("turn down the heat", HomeAssistantClimateCommandParser.ClimateAction.CoolDown)]
    [InlineData("it's too hot", HomeAssistantClimateCommandParser.ClimateAction.CoolDown)]
    [InlineData("way too hot", HomeAssistantClimateCommandParser.ClimateAction.CoolDown)]
    [InlineData("can you cool it down", HomeAssistantClimateCommandParser.ClimateAction.CoolDown)]
    [InlineData("cool down", HomeAssistantClimateCommandParser.ClimateAction.CoolDown)]
    [InlineData("make it cooler please", HomeAssistantClimateCommandParser.ClimateAction.CoolDown)]
    public void TryParse_CoolDownPhrases_ReturnExpected(string transcript,
        HomeAssistantClimateCommandParser.ClimateAction expectedAction)
    {
        var parsed = HomeAssistantClimateCommandParser.TryParse(transcript, out var command);

        Assert.True(parsed);
        Assert.Equal(expectedAction, command.Action);
        Assert.Equal(HomeAssistantClimateCommandParser.ClimateScope.Room, command.Scope);
        Assert.Null(command.Temperature);
    }

    [Theory]
    [InlineData("it's cold in here", HomeAssistantClimateCommandParser.ClimateAction.WarmUp)]
    [InlineData("its cold in here", HomeAssistantClimateCommandParser.ClimateAction.WarmUp)]
    [InlineData("too cold", HomeAssistantClimateCommandParser.ClimateAction.WarmUp)]
    [InlineData("make it warmer", HomeAssistantClimateCommandParser.ClimateAction.WarmUp)]
    [InlineData("turn up the heat", HomeAssistantClimateCommandParser.ClimateAction.WarmUp)]
    [InlineData("it's too cold", HomeAssistantClimateCommandParser.ClimateAction.WarmUp)]
    [InlineData("way too cold", HomeAssistantClimateCommandParser.ClimateAction.WarmUp)]
    [InlineData("can you warm it up", HomeAssistantClimateCommandParser.ClimateAction.WarmUp)]
    [InlineData("heat it up", HomeAssistantClimateCommandParser.ClimateAction.WarmUp)]
    public void TryParse_WarmUpPhrases_ReturnExpected(string transcript,
        HomeAssistantClimateCommandParser.ClimateAction expectedAction)
    {
        var parsed = HomeAssistantClimateCommandParser.TryParse(transcript, out var command);

        Assert.True(parsed);
        Assert.Equal(expectedAction, command.Action);
        Assert.Equal(HomeAssistantClimateCommandParser.ClimateScope.Room, command.Scope);
        Assert.Null(command.Temperature);
    }

    [Theory]
    [InlineData("what temperature is it in here")]
    [InlineData("what's the temperature in here")]
    [InlineData("what is the temperature in here")]
    [InlineData("what tempature is it in here")]
    [InlineData("what's the temp in here")]
    [InlineData("how hot is it in here")]
    [InlineData("how cold is it in here")]
    [InlineData("how warm is it in here")]
    [InlineData("what's the temperature here")]
    [InlineData("what temperature is it inside")]
    [InlineData("what's the room temperature")]
    public void TryParse_GetTemperatureRoomPhrases_ReturnExpected(string transcript)
    {
        var parsed = HomeAssistantClimateCommandParser.TryParse(transcript, out var command);

        Assert.True(parsed);
        Assert.Equal(HomeAssistantClimateCommandParser.ClimateAction.GetTemperature, command.Action);
        Assert.Equal(HomeAssistantClimateCommandParser.ClimateScope.Room, command.Scope);
        Assert.Null(command.TargetName);
        Assert.Null(command.Temperature);
    }

    [Theory]
    [InlineData("what's the bedroom temperature", "bedroom")]
    [InlineData("what is the living room temperature", "living room")]
    [InlineData("what's the temperature on the bedroom thermostat", "bedroom")]
    public void TryParse_GetTemperatureNamedCommands_ReturnTarget(string transcript, string expectedTarget)
    {
        var parsed = HomeAssistantClimateCommandParser.TryParse(transcript, out var command);

        Assert.True(parsed);
        Assert.Equal(HomeAssistantClimateCommandParser.ClimateAction.GetTemperature, command.Action);
        Assert.Equal(HomeAssistantClimateCommandParser.ClimateScope.Named, command.Scope);
        Assert.Equal(expectedTarget, command.TargetName);
        Assert.Null(command.Temperature);
    }

    [Theory]
    [InlineData("what is the temperature in redmond oregon")]
    [InlineData("what's the temperature in boston")]
    [InlineData("set the temperature to 30")]
    [InlineData("set the temperature to 100")]
    [InlineData("make it 30")]
    [InlineData("72 degrees")]
    [InlineData("hello jibo")]
    public void TryParse_InvalidOrUnrelatedPhrases_ReturnFalse(string transcript)
    {
        var parsed = HomeAssistantClimateCommandParser.TryParse(transcript, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void FormatTargetForSpeech_AppendsThermostatWhenMissing()
    {
        Assert.Equal("bedroom thermostat", HomeAssistantClimateCommandParser.FormatTargetForSpeech("bedroom"));
        Assert.Equal("bedroom thermostat",
            HomeAssistantClimateCommandParser.FormatTargetForSpeech("bedroom thermostat"));
    }

    [Fact]
    public void FormatTemperatureForSpeech_FormatsWholeNumbers()
    {
        Assert.Equal("69 degrees", HomeAssistantClimateCommandParser.FormatTemperatureForSpeech(69));
        Assert.Equal("72.5 degrees", HomeAssistantClimateCommandParser.FormatTemperatureForSpeech(72.5m));
    }
}
