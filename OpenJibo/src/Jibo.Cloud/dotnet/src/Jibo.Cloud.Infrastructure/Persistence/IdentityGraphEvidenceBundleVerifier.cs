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
    private const string IdentityGraphSigningKey = "open-jibo-local-identity-graph-development-key";

    public static IdentityGraphEvidenceBundleVerification Verify(string? envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope))
        {
            return new IdentityGraphEvidenceBundleVerification
            {
                Errors = ["missing-envelope"]
            };
        }

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

        if (!envelopeVersion.Equals(ExpectedEnvelopeVersion, StringComparison.Ordinal))
            errors.Add("unexpected-envelope-version");
        if (!bundleVersion.Equals(ExpectedBundleVersion, StringComparison.Ordinal))
            errors.Add("unexpected-bundle-version");
        if (!signatureAlgorithm.Equals(ExpectedSignatureAlgorithm, StringComparison.Ordinal))
            errors.Add("unexpected-signature-algorithm");
        if (!signatureKeyId.Equals(ExpectedSignatureKeyId, StringComparison.Ordinal))
            errors.Add("unexpected-signature-key-id");
        if (!sawPayloadEnd || payloadLines.Count == 0) errors.Add("missing-payload");
        if (!bundleHash.Equals(computedBundleHash, StringComparison.OrdinalIgnoreCase))
            errors.Add("bundle-hash-mismatch");
        if (!signature.Equals(computedSignature, StringComparison.OrdinalIgnoreCase))
            errors.Add("bundle-signature-mismatch");

        return new IdentityGraphEvidenceBundleVerification
        {
            IsValid = errors.Count == 0,
            EnvelopeVersion = envelopeVersion,
            BundleVersion = bundleVersion,
            BundleHash = bundleHash,
            ComputedBundleHash = computedBundleHash,
            SignatureAlgorithm = signatureAlgorithm,
            SignatureKeyId = signatureKeyId,
            Signature = signature,
            ComputedSignature = computedSignature,
            AdmissionRecommendation = Get(payloadFields, "admission-recommendation"),
            AdmissionDecisionHash = Get(payloadFields, "admission-decision-hash"),
            SnapshotContentHash = Get(payloadFields, "snapshot-content-hash"),
            AccountId = Get(payloadFields, "account"),
            LoopId = Get(payloadFields, "loop"),
            RobotId = Get(payloadFields, "robot"),
            DeviceId = Get(payloadFields, "device"),
            PeopleCount = GetInt(payloadFields, "people-count"),
            MemberCount = GetInt(payloadFields, "member-count"),
            RelationshipCount = GetInt(payloadFields, "relationship-count"),
            EvidenceSignalCount = GetInt(payloadFields, "evidence-signal-count"),
            BlockingEvidence = SplitCsv(Get(payloadFields, "admission-blocking-evidence")),
            Errors = errors
        };
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

    private static string Get(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : string.Empty;

    private static int GetInt(IReadOnlyDictionary<string, string> fields, string key) =>
        int.TryParse(Get(fields, key), out var value) ? value : 0;

    private static IReadOnlyList<string> SplitCsv(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ComputeSha256Hex(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    private static string SignPayload(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(IdentityGraphSigningKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
