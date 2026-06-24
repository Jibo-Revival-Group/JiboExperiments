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
        Assert.Equal(1, graph.SnapshotVersion);
        Assert.Matches("^[a-f0-9]{64}$", graph.ContentHash);
        Assert.Contains(graph.People, person => person.PersonId == "person-openjibo-owner" && person.IsPrimary);
        Assert.Contains(graph.Members, member => member.Type == "owner" && member.Status == "active");
        Assert.Contains(graph.Members, member => member.Type == "robot" && member.AccountId == "Ghost-Instance-Onion-Silk");
        Assert.Contains(graph.Relationships, relationship =>
            relationship.SubjectId == "person-openjibo-owner" &&
            relationship.Relationship == "primary-user-of" &&
            relationship.ObjectId == "Ghost-Instance-Onion-Silk");
        Assert.Contains(graph.Relationships, relationship =>
            relationship.SubjectId == graph.AccountId &&
            relationship.SubjectKind == "account" &&
            relationship.Relationship == "owns" &&
            relationship.ObjectId == graph.LoopId);
        Assert.Contains(graph.Relationships, relationship =>
            relationship.SubjectId == graph.LoopId &&
            relationship.SubjectKind == "loop" &&
            relationship.Relationship == "served-by" &&
            relationship.ObjectId == "Ghost-Instance-Onion-Silk");
        Assert.Contains(graph.Relationships, relationship =>
            relationship.SubjectId == "Ghost-Instance-Onion-Silk" &&
            relationship.SubjectKind == "robot" &&
            relationship.Relationship == "runs-on" &&
            relationship.ObjectId == "BOJW-1000-0017-0820-0020");
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
        Assert.Contains(graph.Relationships, relationship =>
            relationship.SubjectId == member.Id &&
            relationship.SubjectKind == "loop-member" &&
            relationship.Relationship == "represented-by" &&
            relationship.ObjectId == "usr-family-member");
    }

    [Fact]
    public void GetIdentityGraph_IncludesEnrollmentRelationshipsForRecognizedLoopMembers()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = "Test Robot"
        });
        var loopId = store.GetLoops()[0].LoopId;
        var member = store.AddLoopMember(loopId, "usr-family-member", "family@example.com", "Grace", "Hopper",
            "unknown", null, false, "family");

        store.SetMemberEnrollment(loopId, member.Id, face: true, voice: true);

        var graph = store.GetIdentityGraph(loopId);

        Assert.Contains(graph.Relationships, relationship =>
            relationship.SubjectId == member.Id &&
            relationship.SubjectKind == "loop-member" &&
            relationship.Relationship == "face-enrolled-with" &&
            relationship.ObjectId == "Ghost-Instance-Onion-Silk" &&
            relationship.ObjectKind == "robot");
        Assert.Contains(graph.Relationships, relationship =>
            relationship.SubjectId == member.Id &&
            relationship.SubjectKind == "loop-member" &&
            relationship.Relationship == "voice-enrolled-with" &&
            relationship.ObjectId == "Ghost-Instance-Onion-Silk" &&
            relationship.ObjectKind == "robot");
    }

    [Fact]
    public void GetIdentityGraph_ContentHashIsStableAndChangesWithEnrollmentEvidence()
    {
        var store = new InMemoryCloudStateStore();
        var loopId = store.GetLoops()[0].LoopId;
        var member = store.AddLoopMember(loopId, "usr-family-member", "family@example.com", "Katherine", "Johnson",
            "unknown", null, false, "family");

        var before = store.GetIdentityGraph(loopId);
        var repeated = store.GetIdentityGraph(loopId);

        store.SetMemberEnrollment(loopId, member.Id, face: true, voice: null);
        var after = store.GetIdentityGraph(loopId);

        Assert.Equal(before.ContentHash, repeated.ContentHash);
        Assert.NotEqual(before.ContentHash, after.ContentHash);
    }

}
