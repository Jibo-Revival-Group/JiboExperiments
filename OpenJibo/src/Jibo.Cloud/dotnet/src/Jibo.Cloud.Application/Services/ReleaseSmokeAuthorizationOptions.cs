using System.Security.Cryptography;
using System.Text;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

public sealed class ReleaseSmokeAuthorizationOptions
{
    public const string FixedPrefix = RobotRegistrationSources.DeploymentSmokePrefix;
    public const string SecretHeaderName = "X-OpenJibo-Release-Smoke-Secret";
    public const string ReplicaInstanceHeaderName = "X-OpenJibo-Replica-Instance";
    public const string ReplicaRevisionHeaderName = "X-OpenJibo-Replica-Revision";

    public bool Enabled { get; set; }
    public string? Secret { get; set; }
    public int MaxConcurrentDevices { get; set; } = 6;
    public SigV4ReplayProbeCredentialOptions SigV4ReplayProbe { get; set; } = new();

    public bool TryAuthorize(string deviceId, string? presentedSecret,
        out DeploymentSmokeRegistrationAuthorization? authorization)
    {
        authorization = null;
        if (!IsAllowedDeviceId(deviceId) || !IsSecretAuthorized(presentedSecret)) return false;
        authorization = new DeploymentSmokeRegistrationAuthorization(deviceId, MaxConcurrentDevices);
        return true;
    }

    public bool IsSecretAuthorized(string? presentedSecret)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(Secret)) return false;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(Secret));
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedSecret ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(expectedHash, presentedHash);
    }

    public bool IsAllowedDeviceId(string deviceId) =>
        RobotRegistrationSources.IsAllowedDeploymentSmokeDeviceId(deviceId, MaxConcurrentDevices);

    public bool TryGetSigV4ReplayProbeCredential(
        string deviceId,
        string? registrationSource,
        string? presentedSecret,
        out AwsSigV4Credential? credential)
    {
        credential = null;
        if (!string.Equals(registrationSource, RobotRegistrationSources.DeploymentSmoke,
                StringComparison.OrdinalIgnoreCase) ||
            !TryAuthorize(deviceId, presentedSecret, out _) ||
            !SigV4ReplayProbe.Enabled ||
            string.IsNullOrWhiteSpace(SigV4ReplayProbe.AccessKeyId) ||
            string.IsNullOrWhiteSpace(SigV4ReplayProbe.SecretAccessKey))
            return false;

        credential = new AwsSigV4Credential(
            SigV4ReplayProbe.AccessKeyId.Trim(),
            SigV4ReplayProbe.SecretAccessKey);
        return true;
    }
}

public sealed class SigV4ReplayProbeCredentialOptions
{
    public bool Enabled { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
}

public sealed class DeploymentSmokeRegistrationAuthorization
{
    internal DeploymentSmokeRegistrationAuthorization(string deviceId, int maxConcurrentDevices)
    {
        DeviceId = deviceId;
        MaxConcurrentDevices = maxConcurrentDevices;
    }

    public string DeviceId { get; }
    public int MaxConcurrentDevices { get; }
}
