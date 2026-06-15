using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Tests.Protocol;

public sealed class ProtocolToTurnContextMapperTests
{
    [Fact]
    public void MapListenMessage_PreservesHouseholdListMetadata()
    {
        var session = new CloudSession
        {
            AccountId = "acct-123",
            DeviceId = "device-123",
            Metadata = new Dictionary<string, object?>
            {
                ["householdListState"] = "awaiting_item",
                ["householdListType"] = "shopping",
                ["householdListDisplayType"] = "grocery"
            }
        };

        var envelope = new WebSocketMessageEnvelope
        {
            HostName = "api.jibo.com",
            Text = """{"data":{"text":"add milk"}}"""
        };

        var turn = ProtocolToTurnContextMapper.MapListenMessage(envelope, session, "LISTEN");

        Assert.Equal("add milk", turn.NormalizedTranscript);
        Assert.Equal("awaiting_item", turn.Attributes["householdListState"]);
        Assert.Equal("shopping", turn.Attributes["householdListType"]);
        Assert.Equal("grocery", turn.Attributes["householdListDisplayType"]);
    }
}