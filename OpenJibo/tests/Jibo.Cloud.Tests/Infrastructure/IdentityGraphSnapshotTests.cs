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
        Assert.Contains("no-local-revocation-evidence", graph.AdmissionAssessment.RevocationChecks);
        Assert.Contains("device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020", graph.AdmissionAssessment.RevocationAnchors);
        Assert.Contains("robot-id:Ghost-Instance-Onion-Silk=Ghost-Instance-Onion-Silk", graph.AdmissionAssessment.RevocationAnchors);
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
        Assert.Equal("deny-by-evidence-v1", graph.EvidenceBundle.AdmissionPolicyVersion);
        Assert.Equal("admit", graph.EvidenceBundle.AdmissionRecommendation);
        Assert.Contains("required-corroborating-evidence-present", graph.EvidenceBundle.AdmissionReasons);
        Assert.Contains("device-id", graph.EvidenceBundle.SatisfiedEvidence);
        Assert.Contains("record-signed-snapshot-for-peer-admission", graph.EvidenceBundle.RecommendedActions);
        Assert.Equal(graph.AdmissionAssessment.RevocationChecks, graph.EvidenceBundle.RevocationChecks);
        Assert.Equal(graph.AdmissionAssessment.RevocationAnchors, graph.EvidenceBundle.RevocationAnchors);
        Assert.Contains($"snapshot-content-hash|{graph.ContentHash}", graph.EvidenceBundle.Payload);
        Assert.Contains("admission-reasons|required-corroborating-evidence-present", graph.EvidenceBundle.Payload);
        Assert.Equal(graph.AdmissionAssessment.RequiredEvidence, graph.EvidenceBundle.RequiredEvidence);
        Assert.Contains("admission-required-evidence|application-version,device-id,host-mapping,robot-id", graph.EvidenceBundle.Payload);
        Assert.Contains("admission-satisfied-evidence|application-version,device-id,host-mapping,robot-id", graph.EvidenceBundle.Payload);
        Assert.Contains("admission-recommended-actions|record-signed-snapshot-for-peer-admission", graph.EvidenceBundle.Payload);
        Assert.Contains("admission-revocation-checks|no-local-revocation-evidence", graph.EvidenceBundle.Payload);
        Assert.Contains("admission-revocation-anchors|device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020,robot-id:Ghost-Instance-Onion-Silk=Ghost-Instance-Onion-Silk", graph.EvidenceBundle.Payload);
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
        Assert.Contains("defer-revocation-admission-until-blocking-evidence-resolved", graph.AdmissionAssessment.RevocationChecks);
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
        Assert.Contains("certificate-thumbprint:Ghost-Instance-Onion-Silk=sha256:robot-cert-thumbprint",
            graph.EvidenceBundle.RevocationAnchors);
        Assert.Contains("issued-identity:Ghost-Instance-Onion-Silk=oji_issued_robot_001",
            graph.EvidenceBundle.RevocationAnchors);
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
        Assert.Contains("member-of:4", graph.EvidenceBundle.RelationshipKinds);
        Assert.Contains("served-by:1", graph.EvidenceBundle.RelationshipKinds);
        Assert.Contains("application-version:1", graph.EvidenceBundle.EvidenceSignalKinds);
        Assert.Contains("host-mapping:1", graph.EvidenceBundle.EvidenceSignalKinds);
        Assert.Empty(graph.EvidenceBundle.BlockingEvidence);
        Assert.Contains($"people-count|{graph.People.Count}", graph.EvidenceBundle.Payload);
        Assert.Contains($"member-count|{graph.Members.Count}", graph.EvidenceBundle.Payload);
        Assert.Contains($"relationship-count|{graph.Relationships.Count}", graph.EvidenceBundle.Payload);
        Assert.Contains($"evidence-signal-count|{graph.EvidenceSignals.Count}", graph.EvidenceBundle.Payload);
        Assert.Contains("relationship-kinds|", graph.EvidenceBundle.Payload);
        Assert.Contains("evidence-signal-kinds|", graph.EvidenceBundle.Payload);
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

    [Fact]
    public void GetIdentityGraph_QuarantinesAdmissionWhenRevocationAnchorMatchesLocalDenyList()
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
        store.RevokeIdentityGraphAnchor("device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020");

        var graph = store.GetIdentityGraph();

        Assert.Equal("quarantine", graph.AdmissionAssessment.Recommendation);
        Assert.Contains("revoked-identity-anchor", graph.AdmissionAssessment.Reasons);
        Assert.Contains("revoked:device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020",
            graph.AdmissionAssessment.BlockingEvidence);
        Assert.Contains(
            "local-revocation-match:device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020",
            graph.AdmissionAssessment.RevocationChecks);
        Assert.Contains("keep-revoked-identity-anchor-quarantined",
            graph.AdmissionAssessment.RecommendedActions);
        Assert.Contains("admission-reasons|revoked-identity-anchor", graph.EvidenceBundle.Payload);
        Assert.Contains("admission-blocking-evidence|revoked:device-id:BOJW-1000-0017-0820-0020=BOJW-1000-0017-0820-0020",
            graph.EvidenceBundle.Payload);
    }

    [Fact]
    public void VerifyEvidenceBundleEnvelope_AcceptsUntamperedOfflinePayload()
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
        var graph = store.GetIdentityGraph();

        var verification = IdentityGraphEvidenceBundleVerifier.Verify(graph.EvidenceBundle.Envelope);

        Assert.True(verification.IsValid);
        Assert.Empty(verification.Errors);
        Assert.Equal("identity-graph-evidence-envelope-v1", verification.EnvelopeVersion);
        Assert.Equal("identity-graph-evidence-bundle-v1", verification.BundleVersion);
        Assert.Equal(graph.EvidenceBundle.BundleHash, verification.ComputedBundleHash);
        Assert.Equal(graph.EvidenceBundle.Signature, verification.ComputedSignature);
        Assert.Equal(graph.EvidenceBundle.AdmissionPolicyVersion, verification.AdmissionPolicyVersion);
        Assert.Equal(graph.EvidenceBundle.AdmissionRecommendation, verification.AdmissionRecommendation);
        Assert.Equal(graph.EvidenceBundle.AdmissionReasons, verification.AdmissionReasons);
        Assert.Equal(graph.EvidenceBundle.RequiredEvidence.Order(StringComparer.Ordinal), verification.RequiredEvidence);
        Assert.Equal(graph.EvidenceBundle.SatisfiedEvidence.Order(StringComparer.Ordinal), verification.SatisfiedEvidence);
        Assert.Equal(graph.EvidenceBundle.RecommendedActions, verification.RecommendedActions);
        Assert.Equal(graph.EvidenceBundle.RevocationChecks, verification.RevocationChecks);
        Assert.Equal(graph.EvidenceBundle.RevocationAnchors, verification.RevocationAnchors);
        Assert.Equal(graph.EvidenceBundle.AdmissionDecisionHash, verification.AdmissionDecisionHash);
        Assert.Equal(graph.EvidenceBundle.AdmissionDecisionHash, verification.ComputedAdmissionDecisionHash);
        Assert.Equal(graph.EvidenceBundle.AdmissionSignature, verification.AdmissionSignature);
        Assert.Equal(graph.EvidenceBundle.AdmissionSignature, verification.ComputedAdmissionSignature);
        Assert.True(verification.AdmissionDecisionSignatureValid);
        Assert.Equal(graph.EvidenceBundle.SnapshotContentHash, verification.SnapshotContentHash);
        Assert.Equal(graph.EvidenceBundle.SnapshotSignature, verification.SnapshotSignature);
        Assert.Equal(graph.EvidenceBundle.SnapshotSignature, verification.ComputedSnapshotSignature);
        Assert.True(verification.SnapshotSignatureValid);
        Assert.Equal(graph.EvidenceBundle.AccountId, verification.AccountId);
        Assert.Equal(graph.EvidenceBundle.LoopId, verification.LoopId);
        Assert.Equal(graph.EvidenceBundle.RobotId, verification.RobotId);
        Assert.Equal(graph.EvidenceBundle.DeviceId, verification.DeviceId);
        Assert.Equal(graph.EvidenceBundle.PeopleCount, verification.PeopleCount);
        Assert.Equal(graph.EvidenceBundle.MemberCount, verification.MemberCount);
        Assert.Equal(graph.EvidenceBundle.RelationshipCount, verification.RelationshipCount);
        Assert.Equal(graph.EvidenceBundle.EvidenceSignalCount, verification.EvidenceSignalCount);
        Assert.Equal(graph.EvidenceBundle.RelationshipKinds, verification.RelationshipKinds);
        Assert.Equal(graph.EvidenceBundle.EvidenceSignalKinds, verification.EvidenceSignalKinds);
        Assert.Empty(verification.BlockingEvidence);
    }

    [Fact]
    public void VerifyEvidenceBundleEnvelope_RejectsTamperedOfflinePayload()
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
        var graph = store.GetIdentityGraph();
        var tamperedEnvelope = graph.EvidenceBundle.Envelope.Replace("admission-recommendation|admit",
            "admission-recommendation|quarantine", StringComparison.Ordinal);

        var verification = IdentityGraphEvidenceBundleVerifier.Verify(tamperedEnvelope);

        Assert.False(verification.IsValid);
        Assert.Contains("bundle-hash-mismatch", verification.Errors);
        Assert.Contains("bundle-signature-mismatch", verification.Errors);
        Assert.Contains("admission-decision-hash-mismatch", verification.Errors);
        Assert.Contains("admission-signature-mismatch", verification.Errors);
        Assert.False(verification.AdmissionDecisionSignatureValid);
        Assert.Equal("quarantine", verification.AdmissionRecommendation);
    }

    [Fact]
    public void VerifyEvidenceBundleEnvelope_RejectsTamperedSnapshotSignature()
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
        var graph = store.GetIdentityGraph();
        var tamperedEnvelope = graph.EvidenceBundle.Envelope.Replace($"snapshot-signature|{graph.EvidenceBundle.SnapshotSignature}",
            "snapshot-signature|0000000000000000000000000000000000000000000000000000000000000000",
            StringComparison.Ordinal);

        var verification = IdentityGraphEvidenceBundleVerifier.Verify(tamperedEnvelope);

        Assert.False(verification.IsValid);
        Assert.Contains("bundle-hash-mismatch", verification.Errors);
        Assert.Contains("bundle-signature-mismatch", verification.Errors);
        Assert.Contains("snapshot-signature-mismatch", verification.Errors);
        Assert.False(verification.SnapshotSignatureValid);
        Assert.Equal(graph.EvidenceBundle.SnapshotSignature, verification.ComputedSnapshotSignature);
    }

    [Fact]
    public void VerifyEvidenceBundleEnvelope_ExtractsBlockingEvidenceForOfflineQuarantineReview()
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

        var verification = IdentityGraphEvidenceBundleVerifier.Verify(graph.EvidenceBundle.Envelope);

        Assert.True(verification.IsValid);
        Assert.Equal("quarantine", verification.AdmissionRecommendation);
        Assert.Contains("host-mapping:neo-hub.jibo.com->neo-hub.jibo.com", verification.BlockingEvidence);
        Assert.Equal(graph.EvidenceBundle.BlockingEvidence, verification.BlockingEvidence);
        Assert.Contains("untrusted-host-mapping-target", verification.AdmissionReasons);
        Assert.Contains("host-mapping", verification.SatisfiedEvidence);
        Assert.Contains("redirect-legacy-host-mapping-to-open-jibo-target", verification.RecommendedActions);
        Assert.Equal(graph.EvidenceBundle.EvidenceSignalCount, verification.EvidenceSignalCount);
    }

}
