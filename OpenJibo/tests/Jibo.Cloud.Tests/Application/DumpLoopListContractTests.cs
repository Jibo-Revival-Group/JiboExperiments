using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Tests.Application;

/// <summary>
/// Synthetic Loop#list shape mirroring the stock SyncManager contract
/// (incoming/outgoing types, flattened names, empty robot account).
/// No real household data — see Fixtures/stock-loop-list-contract.json.
/// </summary>
public sealed class DumpLoopListContractTests
{
    private static readonly JsonDocument DumpList = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures",
            "stock-loop-list-contract.json")));

    [Fact]
    public void DumpFixture_MatchesStockSyncManagerMemberShape()
    {
        var loops = DumpList.RootElement.EnumerateArray().ToArray();
        Assert.Single(loops);
        var loop = loops[0];
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaa", loop.GetProperty("id").GetString());
        Assert.Equal("cccccccccccccccccccccccc", loop.GetProperty("robot").GetString());
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbb", loop.GetProperty("owner").GetString());
        Assert.Equal("Test-Robot-Friendly-Name", loop.GetProperty("robotFriendlyId").GetString());

        var members = loop.GetProperty("members").EnumerateArray().ToArray();
        Assert.NotEmpty(members);

        // _isLoopGood: owner + robot accountIds present
        var accountIds = members
            .Select(m => m.TryGetProperty("accountId", out var id) ? id.GetString() : null)
            .Where(static v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
        Assert.Contains(loop.GetProperty("owner").GetString()!, accountIds!);
        Assert.Contains(loop.GetProperty("robot").GetString()!, accountIds!);

        Assert.Contains(members, m => m.GetProperty("type").GetString() == "incoming");
        Assert.Contains(members, m => m.GetProperty("type").GetString() == "outgoing");

        var robot = members.Single(m =>
            m.GetProperty("accountId").GetString() == loop.GetProperty("robot").GetString());
        Assert.Equal("outgoing", robot.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, robot.GetProperty("account").ValueKind);
        Assert.False(robot.GetProperty("account").EnumerateObject().Any());

        var owner = members.Single(m => m.GetProperty("type").GetString() == "incoming");
        Assert.Equal("Alex", owner.GetProperty("account").GetProperty("firstName").GetString());
        Assert.Equal("Alex", owner.GetProperty("firstName").GetString());

        var household = members.Single(m =>
            m.TryGetProperty("firstName", out var fn) && fn.GetString() == "Pat");
        Assert.Equal("outgoing", household.GetProperty("type").GetString());
        Assert.Equal("Pat", household.GetProperty("account").GetProperty("firstName").GetString());
        Assert.True(household.GetProperty("account").TryGetProperty("isChild", out _));
    }

    [Fact]
    public void MapLoopMember_PortalAddedPerson_MatchesDumpStockShape()
    {
        var mapped = JiboCloudProtocolService.MapLoopMember(new LoopMemberRecord
        {
            Id = "mbr-bob-ross",
            LoopId = "aaaaaaaaaaaaaaaaaaaaaaaa",
            FirstName = "Bob",
            LastName = "Ross",
            Gender = "male",
            Birthday = 312613200000,
            IsChild = false,
            Type = "member",
            Status = "accepted"
        });

        var json = JsonSerializer.SerializeToElement(mapped);
        Assert.Equal("outgoing", json.GetProperty("type").GetString());
        Assert.Equal("Bob", json.GetProperty("account").GetProperty("firstName").GetString());
        Assert.Equal("Ross", json.GetProperty("account").GetProperty("lastName").GetString());
        Assert.Equal("Bob", json.GetProperty("firstName").GetString());
        Assert.Equal("Ross", json.GetProperty("lastName").GetString());
        Assert.Equal("male", json.GetProperty("gender").GetString());
        Assert.False(json.GetProperty("isChild").GetBoolean());
        Assert.Equal("accepted", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("account").TryGetProperty("isChild", out _));
    }

    [Fact]
    public void MapLoopMember_Robot_MatchesDumpEmptyAccountOutgoing()
    {
        var mapped = JiboCloudProtocolService.MapLoopMember(new LoopMemberRecord
        {
            Id = "dddddddddddddddddddddddd",
            LoopId = "aaaaaaaaaaaaaaaaaaaaaaaa",
            AccountId = "cccccccccccccccccccccccc",
            Type = "robot",
            Status = "accepted"
        });

        var json = JsonSerializer.SerializeToElement(mapped);
        Assert.Equal("outgoing", json.GetProperty("type").GetString());
        Assert.Equal("cccccccccccccccccccccccc", json.GetProperty("accountId").GetString());
        Assert.False(json.GetProperty("account").EnumerateObject().Any());
        Assert.False(json.TryGetProperty("firstName", out _));
    }

    [Theory]
    [InlineData("owner", "incoming")]
    [InlineData("member", "outgoing")]
    [InlineData("robot", "outgoing")]
    [InlineData("incoming", "incoming")]
    [InlineData("outgoing", "outgoing")]
    public void MapLoopMemberWireType_TranslatesInternalRoles(string internalType, string expected)
    {
        Assert.Equal(expected, JiboCloudProtocolService.MapLoopMemberWireType(internalType));
    }
}
