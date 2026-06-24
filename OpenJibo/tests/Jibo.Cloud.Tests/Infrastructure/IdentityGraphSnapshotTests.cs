using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class IdentityGraphSnapshotTests
{
    [Fact]
    public void GetIdentityGraph_IncludesDefaultLoopRobotPeopleAndRelationships()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = "Test Robot"
        });

        var graph = store.GetIdentityGraph();

        Assert.Equal("usr_openjibo_owner", graph.AccountId);
        Assert.Equal("BOJW-1000-0017-0820-0020", graph.DeviceId);
        Assert.Equal("Ghost-Instance-Onion-Silk", graph.RobotId);
        Assert.Contains(graph.People, person => person.PersonId == "person-openjibo-owner" && person.IsPrimary);
        Assert.Contains(graph.Members, member => member.Type == "owner" && member.Status == "active");
        Assert.Contains(graph.Members, member => member.Type == "robot" && member.AccountId == "Ghost-Instance-Onion-Silk");
        Assert.Contains(graph.Relationships, relationship =>
            relationship.SubjectId == "person-openjibo-owner" &&
            relationship.Relationship == "primary-user-of" &&
            relationship.ObjectId == "Ghost-Instance-Onion-Silk");
    }

    [Fact]
    public void GetIdentityGraph_IncludesAddedLoopMemberAsActiveRelationship()
    {
        var store = new InMemoryCloudStateStore();
        var loopId = store.GetLoops()[0].LoopId;
        var member = store.AddLoopMember(loopId, "usr-family-member", "family@example.com", "Ada", "Lovelace",
            "unknown", null, false, "family");

        var graph = store.GetIdentityGraph(loopId);

        Assert.Contains(graph.Members, item => item.Id == member.Id && item.FirstName == "Ada");
        Assert.Contains(graph.Relationships, relationship =>
            relationship.SubjectId == "usr-family-member" &&
            relationship.SubjectKind == "family" &&
            relationship.Relationship == "member-of" &&
            relationship.ObjectId == loopId);
    }
}
