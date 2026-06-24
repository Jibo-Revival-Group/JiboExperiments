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

    public IDictionary<string, string> HostMappings { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}