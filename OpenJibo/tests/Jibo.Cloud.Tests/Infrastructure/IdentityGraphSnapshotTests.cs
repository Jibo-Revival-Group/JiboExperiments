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
            FriendlyName = "Test Robot",
            FirmwareVersion = "1.9.2",
            ApplicationVersion = "1.0.20",
            HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["neo-hub.jibo.com"] = "openjibo.local"
            }
        });

        var graph = store.GetIdentityGraph();

        Assert.Equal("usr_openjibo_owner", graph.AccountId);
        Assert.Equal("BOJW-1000-0017-0820-0020", graph.DeviceId);
        Assert.Equal("Ghost-Instance-Onion-Silk", graph.RobotId);
        Assert.Equal(1, graph.SnapshotVersion);
        Assert.Matches("^[a-f0-9]{64}$", graph.ContentHash);
        Assert.Equal("HMAC-SHA256", graph.SignatureAlgorithm);
        Assert.Equal("open-jibo-local-snapshot-v1", graph.SignatureKeyId);
        Assert.Equal($"1|{graph.AccountId}|{graph.LoopId}|{graph.ContentHash}", graph.SignaturePayload);
        Assert.Matches("^[a-f0-9]{64}$", graph.Signature);
        Assert.Equal("deny-by-evidence-v1", graph.AdmissionAssessment.PolicyVersion);
        Assert.Equal("admit", graph.AdmissionAssessment.Recommendation);
        Assert.Contains("required-corroborating-evidence-present", graph.AdmissionAssessment.Reasons);
        Assert.Contains("device-id", graph.AdmissionAssessment.SatisfiedEvidence);
        Assert.Empty(graph.AdmissionAssessment.BlockingEvidence);
        Assert.Contains("record-signed-snapshot-for-peer-admission", graph.AdmissionAssessment.RecommendedActions);
        Assert.Contains($"content-hash|{graph.ContentHash}", graph.AdmissionAssessment.DecisionPayload);
        Assert.Contains("recommendation|admit", graph.AdmissionAssessment.DecisionPayload);
        Assert.Matches("^[a-f0-9]{64}$", graph.AdmissionAssessment.DecisionHash);
        Assert.Equal("HMAC-SHA256", graph.AdmissionAssessment.SignatureAlgorithm);
        Assert.Equal("open-jibo-local-admission-v1", graph.AdmissionAssessment.SignatureKeyId);
        Assert.Matches("^[a-f0-9]{64}$", graph.AdmissionAssessment.Signature);
        Assert.Equal("identity-graph-evidence-bundle-v1", graph.EvidenceBundle.BundleVersion);
        Assert.Equal(graph.AccountId, graph.EvidenceBundle.AccountId);
        Assert.Equal(graph.LoopId, graph.EvidenceBundle.LoopId);
        Assert.Equal(graph.RobotId, graph.EvidenceBundle.RobotId);
        Assert.Equal(graph.DeviceId, graph.EvidenceBundle.DeviceId);
        Assert.Equal(graph.ContentHash, graph.EvidenceBundle.SnapshotContentHash);
        Assert.Equal(graph.Signature, graph.EvidenceBundle.SnapshotSignature);
        Assert.Equal(graph.AdmissionAssessment.DecisionHash, graph.EvidenceBundle.AdmissionDecisionHash);
        Assert.Equal(graph.AdmissionAssessment.Signature, graph.EvidenceBundle.AdmissionSignature);
        Assert.Equal("admit", graph.EvidenceBundle.AdmissionRecommendation);
        Assert.Contains($"snapshot-content-hash|{graph.ContentHash}", graph.EvidenceBundle.Payload);
        Assert.Contains($"admission-decision-hash|{graph.AdmissionAssessment.DecisionHash}", graph.EvidenceBundle.Payload);
        Assert.Matches("^[a-f0-9]{64}$", graph.EvidenceBundle.BundleHash);
        Assert.Equal("HMAC-SHA256", graph.EvidenceBundle.SignatureAlgorithm);
        Assert.Equal("open-jibo-local-evidence-bundle-v1", graph.EvidenceBundle.SignatureKeyId);
        Assert.Matches("^[a-f0-9]{64}$", graph.EvidenceBundle.Signature);
        Assert.Contains("envelope-version|identity-graph-evidence-envelope-v1", graph.EvidenceBundle.Envelope);
        Assert.Contains($"bundle-hash|{graph.EvidenceBundle.BundleHash}", graph.EvidenceBundle.Envelope);
        Assert.Contains($"bundle-signature|{graph.EvidenceBundle.Signature}", graph.EvidenceBundle.Envelope);
        Assert.Contains("payload-begin", graph.EvidenceBundle.Envelope);
        Assert.Contains(graph.EvidenceBundle.Payload, graph.EvidenceBundle.Envelope);
        Assert.Contains("payload-end", graph.EvidenceBundle.Envelope);
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
        Assert.Contains(graph.EvidenceSignals, signal =>
            signal.SignalKind == "device-id" &&
            signal.SignalId == "BOJW-1000-0017-0820-0020" &&
            signal.Role == "corroborating");
        Assert.Contains(graph.EvidenceSignals, signal =>
            signal.SignalKind == "firmware-version" && signal.Value == "1.9.2");
        Assert.Contains(graph.EvidenceSignals, signal =>
            signal.SignalKind == "host-mapping" &&
            signal.SignalId == "neo-hub.jibo.com" &&
            signal.Value == "openjibo.local");
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
    public void GetIdentityGraph_IncludesGuardianRelationshipsForChildMembers()
    {
        var store = new InMemoryCloudStateStore();
        var loopId = store.GetLoops()[0].LoopId;
        var guardian = store.AddLoopMember(loopId, "usr-guardian", "guardian@example.com", "Mary", "Jackson",
            "unknown", null, false, "family");
        var child = store.AddLoopMember(loopId, "usr-child", "child@example.com", "Dorothy", "Vaughan",
            "unknown", null, true, "family", guardian.Id);

        var graph = store.GetIdentityGraph(loopId);

        Assert.Contains(graph.Relationships, relationship =>
            relationship.SubjectId == child.Id &&
            relationship.SubjectKind == "loop-member" &&
            relationship.Relationship == "dependent-of" &&
            relationship.ObjectId == guardian.Id &&
            relationship.ObjectKind == "loop-member");
        Assert.Contains(graph.Relationships, relationship =>
            relationship.SubjectId == guardian.Id &&
            relationship.SubjectKind == "loop-member" &&
            relationship.Relationship == "guardian-of" &&
            relationship.ObjectId == child.Id &&
            relationship.ObjectKind == "loop-member");
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
        Assert.Equal(before.SignaturePayload, repeated.SignaturePayload);
        Assert.NotEqual(before.SignaturePayload, after.SignaturePayload);
        Assert.Equal(before.Signature, repeated.Signature);
        Assert.NotEqual(before.Signature, after.Signature);
        Assert.NotEqual(before.AdmissionAssessment.DecisionPayload, after.AdmissionAssessment.DecisionPayload);
        Assert.NotEqual(before.AdmissionAssessment.DecisionHash, after.AdmissionAssessment.DecisionHash);
        Assert.NotEqual(before.AdmissionAssessment.Signature, after.AdmissionAssessment.Signature);
    }

    [Fact]
    public void GetIdentityGraph_SignatureIsScopedToLoopAndContentHash()
    {
        var store = new InMemoryCloudStateStore();
        var defaultLoopId = store.GetLoops()[0].LoopId;
        var defaultGraph = store.GetIdentityGraph(defaultLoopId);
        const string alternateLoopId = "loop-secondary-test";
        var alternateGraph = store.GetIdentityGraph(alternateLoopId);

        Assert.Equal(defaultGraph.AccountId, alternateGraph.AccountId);
        Assert.NotEqual(defaultGraph.LoopId, alternateGraph.LoopId);
        Assert.NotEqual(defaultGraph.ContentHash, alternateGraph.ContentHash);
        Assert.NotEqual(defaultGraph.SignaturePayload, alternateGraph.SignaturePayload);
        Assert.NotEqual(defaultGraph.Signature, alternateGraph.Signature);
    }

    [Fact]
    public void GetIdentityGraph_ContentHashChangesWithCorroboratingDeviceEvidence()
    {
        var store = new InMemoryCloudStateStore();
        var loopId = store.GetLoops()[0].LoopId;

        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            ApplicationVersion = "1.0.20"
        });
        var before = store.GetIdentityGraph(loopId);

        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            ApplicationVersion = "1.0.21"
        });
        var after = store.GetIdentityGraph(loopId);

        Assert.NotEqual(before.ContentHash, after.ContentHash);
        Assert.Contains(after.EvidenceSignals, signal =>
            signal.SignalKind == "application-version" && signal.Value == "1.0.21");
    }

    [Fact]
    public void GetIdentityGraph_QuarantinesAdmissionWhenRequiredCorroboratingEvidenceIsMissing()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk"
        });

        var graph = store.GetIdentityGraph();

        Assert.Equal("quarantine", graph.AdmissionAssessment.Recommendation);
        Assert.Contains("missing-application-version", graph.AdmissionAssessment.Reasons);
        Assert.Contains("missing-host-mapping", graph.AdmissionAssessment.Reasons);
        Assert.Contains("device-id", graph.AdmissionAssessment.RequiredEvidence);
        Assert.Contains("device-id", graph.AdmissionAssessment.SatisfiedEvidence);
        Assert.Contains("required:application-version", graph.AdmissionAssessment.BlockingEvidence);
        Assert.Contains("required:host-mapping", graph.AdmissionAssessment.BlockingEvidence);
        Assert.Contains("capture-current-open-jibo-application-version", graph.AdmissionAssessment.RecommendedActions);
        Assert.Contains("record-open-jibo-host-mapping", graph.AdmissionAssessment.RecommendedActions);
        Assert.Contains("recommendation|quarantine", graph.AdmissionAssessment.DecisionPayload);
        Assert.Contains("blocking-evidence|required:application-version,required:host-mapping", graph.AdmissionAssessment.DecisionPayload);
    }

    [Fact]
    public void GetIdentityGraph_QuarantinesAdmissionWhenHostMappingStillTargetsLegacyCloud()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            ApplicationVersion = "1.0.20",
            HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["neo-hub.jibo.com"] = "neo-hub.jibo.com"
            }
        });

        var graph = store.GetIdentityGraph();

        Assert.Equal("quarantine", graph.AdmissionAssessment.Recommendation);
        Assert.Contains("untrusted-host-mapping-target", graph.AdmissionAssessment.Reasons);
        Assert.Contains("host-mapping", graph.AdmissionAssessment.SatisfiedEvidence);
        Assert.Contains("host-mapping:neo-hub.jibo.com->neo-hub.jibo.com", graph.AdmissionAssessment.BlockingEvidence);
        Assert.Contains("redirect-legacy-host-mapping-to-open-jibo-target", graph.AdmissionAssessment.RecommendedActions);
        Assert.Contains(graph.EvidenceSignals, signal =>
            signal.SignalKind == "host-mapping" &&
            signal.SignalId == "neo-hub.jibo.com" &&
            signal.Value == "neo-hub.jibo.com");
    }

    [Fact]
    public void GetIdentityGraph_IncludesOptionalCloneDetectionCorroboratingSignals()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            ApplicationVersion = "1.0.20",
            CertificateThumbprint = "sha256:robot-cert-thumbprint",
            IssuedIdentityId = "oji_issued_robot_001",
            BuildHash = "build-sha256-001",
            ConfigHash = "config-sha256-001",
            HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["neo-hub.jibo.com"] = "openjibo.local"
            }
        });

        var graph = store.GetIdentityGraph();

        Assert.Equal("admit", graph.AdmissionAssessment.Recommendation);
        Assert.Contains(graph.EvidenceSignals, signal =>
            signal.SignalKind == "certificate-thumbprint" &&
            signal.SignalId == "Ghost-Instance-Onion-Silk" &&
            signal.Value == "sha256:robot-cert-thumbprint" &&
            signal.Role == "corroborating");
        Assert.Contains(graph.EvidenceSignals, signal =>
            signal.SignalKind == "issued-identity" && signal.Value == "oji_issued_robot_001");
        Assert.Contains(graph.EvidenceSignals, signal =>
            signal.SignalKind == "build-hash" && signal.Value == "build-sha256-001");
        Assert.Contains(graph.EvidenceSignals, signal =>
            signal.SignalKind == "config-hash" && signal.Value == "config-sha256-001");
    }

    [Fact]
    public void GetIdentityGraph_ContentHashChangesWithCloneDetectionCorroboratingSignals()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            ApplicationVersion = "1.0.20",
            HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["neo-hub.jibo.com"] = "openjibo.local"
            }
        });
        var before = store.GetIdentityGraph();

        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            ApplicationVersion = "1.0.20",
            CertificateThumbprint = "sha256:robot-cert-thumbprint",
            IssuedIdentityId = "oji_issued_robot_001",
            BuildHash = "build-sha256-001",
            ConfigHash = "config-sha256-001",
            HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["neo-hub.jibo.com"] = "openjibo.local"
            }
        });
        var after = store.GetIdentityGraph();

        Assert.NotEqual(before.ContentHash, after.ContentHash);
        Assert.NotEqual(before.Signature, after.Signature);
        Assert.NotEqual(before.AdmissionAssessment.DecisionHash, after.AdmissionAssessment.DecisionHash);
    }

    [Fact]
    public void GetIdentityGraph_EvidenceBundleChangesWithAdmissionDecision()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            ApplicationVersion = "1.0.20",
            HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["neo-hub.jibo.com"] = "openjibo.local"
            }
        });
        var admitted = store.GetIdentityGraph();

        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk"
        });
        var quarantined = store.GetIdentityGraph();

        Assert.Equal("admit", admitted.EvidenceBundle.AdmissionRecommendation);
        Assert.Equal("quarantine", quarantined.EvidenceBundle.AdmissionRecommendation);
        Assert.NotEqual(admitted.EvidenceBundle.Payload, quarantined.EvidenceBundle.Payload);
        Assert.NotEqual(admitted.EvidenceBundle.Envelope, quarantined.EvidenceBundle.Envelope);
        Assert.NotEqual(admitted.EvidenceBundle.BundleHash, quarantined.EvidenceBundle.BundleHash);
        Assert.NotEqual(admitted.EvidenceBundle.Signature, quarantined.EvidenceBundle.Signature);
    }

    [Fact]
    public void GetIdentityGraph_EvidenceBundleCarriesSnapshotSummaryForOfflineAdmissionReview()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            ApplicationVersion = "1.0.20",
            HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["neo-hub.jibo.com"] = "openjibo.local"
            }
        });
        var loopId = store.GetLoops()[0].LoopId;
        store.AddLoopMember(loopId, "usr-family-member", "family@example.com", "Mae", "Jemison",
            "unknown", null, false, "family");

        var graph = store.GetIdentityGraph(loopId);

        Assert.Equal(graph.People.Count, graph.EvidenceBundle.PeopleCount);
        Assert.Equal(graph.Members.Count, graph.EvidenceBundle.MemberCount);
        Assert.Equal(graph.Relationships.Count, graph.EvidenceBundle.RelationshipCount);
        Assert.Equal(graph.EvidenceSignals.Count, graph.EvidenceBundle.EvidenceSignalCount);
        Assert.Empty(graph.EvidenceBundle.BlockingEvidence);
        Assert.Contains($"people-count|{graph.People.Count}", graph.EvidenceBundle.Payload);
        Assert.Contains($"member-count|{graph.Members.Count}", graph.EvidenceBundle.Payload);
        Assert.Contains($"relationship-count|{graph.Relationships.Count}", graph.EvidenceBundle.Payload);
        Assert.Contains($"evidence-signal-count|{graph.EvidenceSignals.Count}", graph.EvidenceBundle.Payload);
        Assert.Contains("admission-blocking-evidence|", graph.EvidenceBundle.Payload);
    }

    [Fact]
    public void GetIdentityGraph_EvidenceBundleCarriesBlockingEvidenceForOfflineQuarantineReview()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            ApplicationVersion = "1.0.20",
            HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["neo-hub.jibo.com"] = "neo-hub.jibo.com"
            }
        });

        var graph = store.GetIdentityGraph();

        Assert.Contains("host-mapping:neo-hub.jibo.com->neo-hub.jibo.com", graph.EvidenceBundle.BlockingEvidence);
        Assert.Contains("admission-blocking-evidence|host-mapping:neo-hub.jibo.com->neo-hub.jibo.com",
            graph.EvidenceBundle.Payload);
    }

}
