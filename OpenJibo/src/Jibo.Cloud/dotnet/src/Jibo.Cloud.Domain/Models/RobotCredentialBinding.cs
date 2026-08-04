namespace Jibo.Cloud.Domain.Models;

/// <summary>
/// A deliberately claimed association between an observed, one-way credential fingerprint and a robot record.
/// Credentials observed on protocol traffic must never create this binding automatically.
/// </summary>
public sealed record RobotCredentialBinding(
    string AccessKeyFingerprint,
    string DeviceId,
    DateTimeOffset ClaimedUtc,
    string ClaimSource);
