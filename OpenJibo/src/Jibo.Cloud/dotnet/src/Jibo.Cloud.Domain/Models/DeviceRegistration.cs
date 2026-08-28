namespace Jibo.Cloud.Domain.Models;

public sealed class DeviceRegistration
{
    public string DeviceId { get; init; } = string.Empty;
    public string RobotId { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = "OpenJibo Dev Robot";
    public string? FirmwareVersion { get; init; }
    public string? ApplicationVersion { get; init; }
    public bool IsActive { get; init; } = true;
    public string? CertificateThumbprint { get; init; }
    public string? IssuedIdentityId { get; init; }
    public string? BuildHash { get; init; }
    public string? ConfigHash { get; init; }
    // Recorded only from an explicit verified OOBE/recovery claim; never inferred from traffic.
    public string? VerifiedSerialNumber { get; init; }
    public string? SerialEvidenceSource { get; init; }
    public DateTimeOffset? SerialEvidenceVerifiedUtc { get; init; }
    public string RegistrationSource { get; init; } = RobotRegistrationSources.Unknown;
    public bool IsHidden { get; init; }
    public DateTimeOffset? ArchivedUtc { get; init; }

    public IDictionary<string, string> HostMappings { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public static class RobotRegistrationSources
{
    public const string DeploymentSmokePrefix = "open-jibo-smoke-staging";
    public const string Unknown = "unknown";
    public const string Physical = "physical";
    public const string BrowserHarness = "browser-harness";
    public const string DeploymentSmoke = "deployment-smoke";
    public const string Bootstrap = "bootstrap";
    public const string Portal = "portal";

    public static string Normalize(string? source, string? deviceId = null)
    {
        var normalized = source?.Trim().ToLowerInvariant();
        if (normalized is Physical or BrowserHarness or DeploymentSmoke or Bootstrap or Portal)
            return normalized;

        var id = deviceId?.Trim() ?? string.Empty;
        if (id.StartsWith("fake-jibo-", StringComparison.OrdinalIgnoreCase)) return BrowserHarness;
        if (id.StartsWith("open-jibo-smoke-", StringComparison.OrdinalIgnoreCase)) return DeploymentSmoke;
        if (id.StartsWith("openjibo-dev-", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("openjibo-bootstrap-", StringComparison.OrdinalIgnoreCase)) return Bootstrap;
        return Unknown;
    }

    public static bool IsRecoverable(string? source, string? deviceId) =>
        !IsSynthetic(Normalize(source, deviceId));

    public static bool IsSynthetic(string source) =>
        string.Equals(source, BrowserHarness, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, DeploymentSmoke, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, Bootstrap, StringComparison.OrdinalIgnoreCase);

    public static bool IsDeploymentSmokeNamespace(string? deviceId) =>
        !string.IsNullOrWhiteSpace(deviceId) &&
        deviceId.Trim().StartsWith($"{DeploymentSmokePrefix}-", StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowedDeploymentSmokeDeviceId(string? deviceId, int maxConcurrentDevices)
    {
        var normalized = deviceId?.Trim() ?? string.Empty;
        if (string.Equals(normalized, $"{DeploymentSmokePrefix}-primary", StringComparison.Ordinal)) return true;
        var prefix = $"{DeploymentSmokePrefix}-concurrent-";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var suffix = normalized[prefix.Length..];
        return int.TryParse(suffix, out var index) && index >= 1 &&
               index <= Math.Clamp(maxConcurrentDevices, 1, 50) &&
               suffix == index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
