using System.Security.Cryptography;
using System.Text;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public static class IdentityGraphEvidenceBundleVerifier
{
    private const string ExpectedEnvelopeVersion = "identity-graph-evidence-envelope-v1";
    private const string ExpectedBundleVersion = "identity-graph-evidence-bundle-v1";
    private const string ExpectedSignatureAlgorithm = "HMAC-SHA256";
    private const string ExpectedSignatureKeyId = "open-jibo-local-evidence-bundle-v1";
    private const string ExpectedSnapshotSignatureKeyId = "open-jibo-local-snapshot-v1";
    private const string ExpectedAdmissionSignatureKeyId = "open-jibo-local-admission-v1";
    private const string IdentityGraphSigningKey = "open-jibo-local-identity-graph-development-key";

    public static IdentityGraphEvidenceBundleVerification Verify(string? envelope,
        IEnumerable<string>? localRevokedAnchors = null)
    {
        if (string.IsNullOrWhiteSpace(envelope))
            return new IdentityGraphEvidenceBundleVerification
            {
                IsLocallyAdmissible = false,
                EffectiveAdmissionRecommendation = "quarantine",
                Errors = ["missing-envelope"]
            };

        var lines = envelope.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var payloadLines = new List<string>();
        var inPayload = false;
        var sawPayloadEnd = false;

        foreach (var line in lines)
        {
            if (line.Equals("payload-begin", StringComparison.Ordinal))
            {
                inPayload = true;
                continue;
            }

            if (line.Equals("payload-end", StringComparison.Ordinal))
            {
                sawPayloadEnd = true;
                inPayload = false;
                continue;
            }

            if (inPayload)
            {
                payloadLines.Add(line);
                continue;
            }

            var separator = line.IndexOf('|', StringComparison.Ordinal);
            if (separator > 0)
                header[line[..separator]] = line[(separator + 1)..];
        }

        var payload = string.Join('\n', payloadLines);
        var payloadFields = ParsePipeFields(payloadLines);
        var errors = new List<string>();
        var envelopeVersion = Get(header, "envelope-version");
        var bundleHash = Get(header, "bundle-hash");
        var signatureAlgorithm = Get(header, "bundle-signature-algorithm");
        var signatureKeyId = Get(header, "bundle-signature-key-id");
        var signature = Get(header, "bundle-signature");
        var computedBundleHash = ComputeSha256Hex(payload);
        var computedSignature = SignPayload(payload);
        var bundleVersion = Get(payloadFields, "bundle-version");
        var accountId = Get(payloadFields, "account");
        var loopId = Get(payloadFields, "loop");
        var snapshotVersion = Get(payloadFields, "snapshot-version");
        var snapshotContentHash = Get(payloadFields, "snapshot-content-hash");
        var snapshotSignatureKeyId = Get(payloadFields, "snapshot-signature-key-id");
        var snapshotSignature = Get(payloadFields, "snapshot-signature");
        var computedSnapshotSignature = SignPayload($"{snapshotVersion}|{accountId}|{loopId}|{snapshotContentHash}");
        var admissionPolicyVersion = Get(payloadFields, "admission-policy-version");
        var admissionRecommendation = Get(payloadFields, "admission-recommendation");
        var admissionReasons = SplitCsv(Get(payloadFields, "admission-reasons"));
        var requiredEvidence = SplitCsv(Get(payloadFields, "admission-required-evidence"));
        if (requiredEvidence.Count == 0)
            requiredEvidence = ["application-version", "device-id", "host-mapping", "robot-id"];
        var satisfiedEvidence = SplitCsv(Get(payloadFields, "admission-satisfied-evidence"));
        var blockingEvidence = SplitCsv(Get(payloadFields, "admission-blocking-evidence"));
        var recommendedActions = SplitCsv(Get(payloadFields, "admission-recommended-actions"));
        var revocationChecks = SplitCsv(Get(payloadFields, "admission-revocation-checks"));
        var revocationAnchors = SplitCsv(Get(payloadFields, "admission-revocation-anchors"));
        var revocationListHash = Get(payloadFields, "admission-revocation-list-hash");
        var peerTransportStatus = Get(payloadFields, "peer-transport-status");
        var replicationReadiness = Get(payloadFields, "replication-readiness");
        var syncDirection = Get(payloadFields, "sync-direction");
        var peerAdmissionMode = Get(payloadFields, "peer-admission-mode");
        var retentionPolicy = Get(payloadFields, "retention-policy");
        var directPeerTransportAllowed = GetBool(payloadFields, "direct-peer-transport-allowed");
        var admissionDecisionHash = Get(payloadFields, "admission-decision-hash");
        var admissionSignatureKeyId = Get(payloadFields, "admission-signature-key-id");
        var admissionSignature = Get(payloadFields, "admission-signature");
        var computedAdmissionDecisionPayload = BuildAdmissionDecisionPayload(accountId, loopId, snapshotContentHash,
            admissionPolicyVersion, admissionRecommendation, admissionReasons, requiredEvidence, satisfiedEvidence,
            blockingEvidence, recommendedActions, revocationChecks, revocationAnchors, revocationListHash);
        var computedAdmissionDecisionHash = ComputeSha256Hex(computedAdmissionDecisionPayload);
        var computedAdmissionSignature = SignPayload(computedAdmissionDecisionPayload);

        if (!envelopeVersion.Equals(ExpectedEnvelopeVersion, StringComparison.Ordinal))
            errors.Add("unexpected-envelope-version");
        if (!bundleVersion.Equals(ExpectedBundleVersion, StringComparison.Ordinal))
            errors.Add("unexpected-bundle-version");
        if (!signatureAlgorithm.Equals(ExpectedSignatureAlgorithm, StringComparison.Ordinal))
            errors.Add("unexpected-signature-algorithm");
        if (!signatureKeyId.Equals(ExpectedSignatureKeyId, StringComparison.Ordinal))
            errors.Add("unexpected-signature-key-id");
        if (!snapshotSignatureKeyId.Equals(ExpectedSnapshotSignatureKeyId, StringComparison.Ordinal))
            errors.Add("unexpected-snapshot-signature-key-id");
        if (!admissionSignatureKeyId.Equals(ExpectedAdmissionSignatureKeyId, StringComparison.Ordinal))
            errors.Add("unexpected-admission-signature-key-id");
        if (!sawPayloadEnd || payloadLines.Count == 0) errors.Add("missing-payload");
        if (!bundleHash.Equals(computedBundleHash, StringComparison.OrdinalIgnoreCase))
            errors.Add("bundle-hash-mismatch");
        if (!signature.Equals(computedSignature, StringComparison.OrdinalIgnoreCase))
            errors.Add("bundle-signature-mismatch");
        if (!snapshotSignature.Equals(computedSnapshotSignature, StringComparison.OrdinalIgnoreCase))
            errors.Add("snapshot-signature-mismatch");
        if (!admissionDecisionHash.Equals(computedAdmissionDecisionHash, StringComparison.OrdinalIgnoreCase))
            errors.Add("admission-decision-hash-mismatch");
        if (!admissionSignature.Equals(computedAdmissionSignature, StringComparison.OrdinalIgnoreCase))
            errors.Add("admission-signature-mismatch");
        if (!peerTransportStatus.Equals("not-enabled", StringComparison.OrdinalIgnoreCase))
            errors.Add("unexpected-peer-transport-status");
        if (!replicationReadiness.Equals("ready-for-retention", StringComparison.OrdinalIgnoreCase) &&
            !replicationReadiness.Equals("blocked-by-admission", StringComparison.OrdinalIgnoreCase))
            errors.Add("unexpected-replication-readiness");
        if (!syncDirection.Equals("snapshot-retention-only", StringComparison.OrdinalIgnoreCase))
            errors.Add("unexpected-sync-direction");
        if (!peerAdmissionMode.Equals("offline-signed-evidence", StringComparison.OrdinalIgnoreCase))
            errors.Add("unexpected-peer-admission-mode");
        if (!retentionPolicy.Equals("owner-retained-until-peer-admission", StringComparison.OrdinalIgnoreCase))
            errors.Add("unexpected-retention-policy");
        if (directPeerTransportAllowed)
            errors.Add("direct-peer-transport-enabled");

        var localRevocationMatches = MatchLocalRevocationAnchors(revocationAnchors, localRevokedAnchors);
        var effectiveAdmissionRecommendation =
            admissionRecommendation.Equals("admit", StringComparison.OrdinalIgnoreCase)
            && localRevocationMatches.Count == 0
                ? "admit"
                : "quarantine";

        return new IdentityGraphEvidenceBundleVerification
        {
            IsValid = errors.Count == 0,
            IsLocallyAdmissible = errors.Count == 0
                                  && effectiveAdmissionRecommendation.Equals("admit",
                                      StringComparison.OrdinalIgnoreCase),
            EffectiveAdmissionRecommendation = effectiveAdmissionRecommendation,
            EnvelopeVersion = envelopeVersion,
            BundleVersion = bundleVersion,
            BundleHash = bundleHash,
            ComputedBundleHash = computedBundleHash,
            SignatureAlgorithm = signatureAlgorithm,
            SignatureKeyId = signatureKeyId,
            Signature = signature,
            ComputedSignature = computedSignature,
            AdmissionPolicyVersion = admissionPolicyVersion,
            AdmissionRecommendation = admissionRecommendation,
            AdmissionReasons = admissionReasons,
            RequiredEvidence = requiredEvidence,
            SatisfiedEvidence = satisfiedEvidence,
            RecommendedActions = recommendedActions,
            RevocationChecks = revocationChecks,
            RevocationAnchors = revocationAnchors,
            RevocationListHash = revocationListHash,
            PeerTransportStatus = peerTransportStatus,
            ReplicationReadiness = replicationReadiness,
            SyncDirection = syncDirection,
            PeerAdmissionMode = peerAdmissionMode,
            RetentionPolicy = retentionPolicy,
            DirectPeerTransportAllowed = directPeerTransportAllowed,
            LocalRevocationMatches = localRevocationMatches,
            AdmissionDecisionHash = admissionDecisionHash,
            ComputedAdmissionDecisionHash = computedAdmissionDecisionHash,
            AdmissionSignature = admissionSignature,
            ComputedAdmissionSignature = computedAdmissionSignature,
            AdmissionDecisionSignatureValid =
                admissionDecisionHash.Equals(computedAdmissionDecisionHash, StringComparison.OrdinalIgnoreCase) &&
                admissionSignature.Equals(computedAdmissionSignature, StringComparison.OrdinalIgnoreCase),
            SnapshotContentHash = snapshotContentHash,
            SnapshotSignature = snapshotSignature,
            ComputedSnapshotSignature = computedSnapshotSignature,
            SnapshotSignatureValid =
                snapshotSignature.Equals(computedSnapshotSignature, StringComparison.OrdinalIgnoreCase),
            AccountId = accountId,
            LoopId = loopId,
            RobotId = Get(payloadFields, "robot"),
            DeviceId = Get(payloadFields, "device"),
            PeopleCount = GetInt(payloadFields, "people-count"),
            MemberCount = GetInt(payloadFields, "member-count"),
            RelationshipCount = GetInt(payloadFields, "relationship-count"),
            EvidenceSignalCount = GetInt(payloadFields, "evidence-signal-count"),
            RelationshipKinds = SplitCsv(Get(payloadFields, "relationship-kinds")),
            EvidenceSignalKinds = SplitCsv(Get(payloadFields, "evidence-signal-kinds")),
            BlockingEvidence = blockingEvidence,
            Errors = errors
        };
    }

    private static IReadOnlyList<string> MatchLocalRevocationAnchors(IReadOnlyList<string> bundleAnchors,
        IEnumerable<string>? localRevokedAnchors)
    {
        if (bundleAnchors.Count == 0 || localRevokedAnchors is null)
            return [];

        var revoked = new HashSet<string>(
            localRevokedAnchors.Where(anchor => !string.IsNullOrWhiteSpace(anchor)).Select(anchor => anchor.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (revoked.Count == 0)
            return [];

        return bundleAnchors.Where(revoked.Contains).Order(StringComparer.Ordinal).ToArray();
    }

    private static Dictionary<string, string> ParsePipeFields(IEnumerable<string> lines)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var separator = line.IndexOf('|', StringComparison.Ordinal);
            if (separator > 0) fields[line[..separator]] = line[(separator + 1)..];
        }

        return fields;
    }

    private static string Get(IReadOnlyDictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static bool GetBool(IReadOnlyDictionary<string, string> fields, string key)
    {
        return bool.TryParse(Get(fields, key), out var value) && value;
    }

    private static int GetInt(IReadOnlyDictionary<string, string> fields, string key)
    {
        return int.TryParse(Get(fields, key), out var value) ? value : 0;
    }

    private static IReadOnlyList<string> SplitCsv(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string BuildAdmissionDecisionPayload(string accountId, string loopId, string contentHash,
        string policyVersion, string recommendation, IReadOnlyList<string> reasons,
        IReadOnlyList<string> requiredEvidence,
        IReadOnlyList<string> satisfiedEvidence, IReadOnlyList<string> blockingEvidence,
        IReadOnlyList<string> recommendedActions, IReadOnlyList<string> revocationChecks,
        IReadOnlyList<string> revocationAnchors, string revocationListHash)
    {
        var lines = new[]
        {
            $"policy-version|{policyVersion}",
            $"account|{accountId}",
            $"loop|{loopId}",
            $"content-hash|{contentHash}",
            $"recommendation|{recommendation}",
            $"reasons|{string.Join(',', reasons.Order(StringComparer.Ordinal))}",
            $"required-evidence|{string.Join(',', requiredEvidence.Order(StringComparer.Ordinal))}",
            $"satisfied-evidence|{string.Join(',', satisfiedEvidence.Order(StringComparer.Ordinal))}",
            $"blocking-evidence|{string.Join(',', blockingEvidence.Order(StringComparer.Ordinal))}",
            $"recommended-actions|{string.Join(',', recommendedActions.Order(StringComparer.Ordinal))}",
            $"revocation-checks|{string.Join(',', revocationChecks.Order(StringComparer.Ordinal))}",
            $"revocation-anchors|{string.Join(',', revocationAnchors.Order(StringComparer.Ordinal))}",
            $"revocation-list-hash|{revocationListHash}"
        };

        return string.Join('\n', lines);
    }

    private static string ComputeSha256Hex(string payload)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string SignPayload(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(IdentityGraphSigningKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
