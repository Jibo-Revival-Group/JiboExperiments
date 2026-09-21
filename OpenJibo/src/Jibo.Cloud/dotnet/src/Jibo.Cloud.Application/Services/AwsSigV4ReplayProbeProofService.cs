using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

public sealed record AwsSigV4ReplayProbeProof(bool Verified, long PriorObservationCount = 0);

/// <summary>
/// Staging release-smoke proof boundary. It authenticates the exact temporary probe
/// credential and then performs one final atomic observation; a returned count of
/// three proves that the identical signed request was already persisted twice.
/// </summary>
public sealed class AwsSigV4ReplayProbeProofService(
    ICloudStateStore stateStore,
    ReleaseSmokeAuthorizationOptions releaseSmokeAuthorization,
    IAwsSigV4ReplayObservationStore observationStore,
    AwsSigV4ReplayDigestKey replayDigestKey)
{
    private static readonly AwsSigV4OperationPolicy Policy = new(
        "Notification.NewRobotToken",
        "us-east-1",
        "jibo",
        ["Notification_20160715.NewRobotToken"],
        ["api.jibo.com", "api.openjibo.com", "open-jibo.jibo.pro", "api.jibo.pro"]);

    public async Task<AwsSigV4ReplayProbeProof> ProveAsync(
        ProtocolEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var body = envelope.TryParseBody();
        var deviceId = body is { ValueKind: JsonValueKind.Object } element &&
                       element.TryGetProperty("deviceId", out var deviceProperty) &&
                       deviceProperty.ValueKind == JsonValueKind.String
            ? deviceProperty.GetString()
            : null;
        var registrationSource = envelope.Headers.TryGetValue(
            "X-OpenJibo-Registration-Source", out var sourceHeader)
            ? sourceHeader
            : null;
        var presentedSecret = envelope.Headers.TryGetValue(
            ReleaseSmokeAuthorizationOptions.SecretHeaderName, out var secretHeader)
            ? secretHeader
            : null;
        if (string.IsNullOrWhiteSpace(deviceId) ||
            !releaseSmokeAuthorization.TryGetSigV4ReplayProbeCredential(
                deviceId,
                registrationSource,
                presentedSecret,
                out var credential))
            return new AwsSigV4ReplayProbeProof(false);

        try
        {
            var verification = new AwsSigV4RequestVerifier(stateStore).Verify(envelope, Policy, credential);
            // The staging ingress FQDN is intentionally not part of the stock-robot host allowlist.
            // It is still covered by the signed Host header; require every other operation binding
            // plus the release-smoke gates above without broadening the production host policy.
            if (!verification.CredentialAuthenticated ||
                !verification.PayloadBound ||
                verification.ScopeClassification != AwsSigV4ScopeClassification.Match ||
                verification.TargetClassification != AwsSigV4TargetClassification.MatchSigned ||
                verification.VerifiedProof is null)
                return new AwsSigV4ReplayProbeProof(false);

            var digest = verification.VerifiedProof.CreateReplayDigest(replayDigestKey, Policy.Operation);
            var result = await observationStore.ObserveAsync(
                digest,
                replayDigestKey.Version,
                Policy.Operation,
                cancellationToken);
            return result.Status == AwsSigV4ReplayObservationStatus.Repeat && result.ObservationCount >= 3
                ? new AwsSigV4ReplayProbeProof(true, result.ObservationCount - 1)
                : new AwsSigV4ReplayProbeProof(false);
        }
        finally
        {
            stateStore.RevokeDeploymentSmokeTokens(
                $"{ReleaseSmokeAuthorizationOptions.FixedPrefix}-primary");
            for (var index = 1; index <= releaseSmokeAuthorization.MaxConcurrentDevices; index++)
                stateStore.RevokeDeploymentSmokeTokens(
                    $"{ReleaseSmokeAuthorizationOptions.FixedPrefix}-concurrent-{index}");
        }
    }
}
