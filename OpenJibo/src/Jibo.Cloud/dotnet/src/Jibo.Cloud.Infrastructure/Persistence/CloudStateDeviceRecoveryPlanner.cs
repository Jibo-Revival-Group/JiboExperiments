using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal static class RecoveryPlanner
{
    public static bool IsRevisionUpdateSuccessful(int affectedRows) => affectedRows == 1;

    public static RecoveryPlan Build(
        IReadOnlyCollection<RecoveryDevice> sourceDevices,
        IReadOnlyCollection<RecoveryAccountDeviceLink> sourceLinks,
        IReadOnlyCollection<RecoveryDeviceMapping> sourceMappings,
        IReadOnlyCollection<string> existingDeviceIds,
        IReadOnlyCollection<string> existingAccountIds,
        IReadOnlyCollection<(string AccountId, string DeviceId)> existingLinks,
        IReadOnlyCollection<(string DeviceId, string MappingKey)> existingMappings)
    {
        var eligible = sourceDevices.Where(device =>
            RobotRegistrationSources.IsRecoverable(device.RegistrationSource, device.DeviceId)).ToArray();
        var excluded = sourceDevices.Count - eligible.Length;
        var existingDevices = existingDeviceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = eligible.Where(device => !existingDevices.Contains(device.DeviceId)).ToArray();
        var missingIds = missing.Select(device => device.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var links = sourceLinks.Where(link => missingIds.Contains(link.DeviceId))
            .DistinctBy(link => (link.AccountId, link.DeviceId)).ToArray();
        var mappings = sourceMappings.Where(mapping => missingIds.Contains(mapping.DeviceId))
            .DistinctBy(mapping => (mapping.DeviceId, mapping.MappingKey)).ToArray();
        var accounts = existingAccountIds.ToHashSet(StringComparer.Ordinal);
        var knownLinks = existingLinks.ToHashSet();
        var knownMappings = existingMappings.ToHashSet();
        var linksToInsert = links.Where(link => accounts.Contains(link.AccountId) &&
            !knownLinks.Contains((link.AccountId, link.DeviceId))).ToArray();
        var mappingsToInsert = mappings.Where(mapping =>
            !knownMappings.Contains((mapping.DeviceId, mapping.MappingKey))).ToArray();

        var alreadyPresent = eligible.Count(device => existingDevices.Contains(device.DeviceId));

        return new RecoveryPlan(sourceDevices.Count, eligible, excluded, alreadyPresent, missing,
            links, linksToInsert, links.Count(link => !accounts.Contains(link.AccountId)), mappings,
            mappingsToInsert);
    }
}

internal sealed record RecoveryDevice(
    string DeviceId, string RobotId, string FriendlyName, string? FirmwareVersion,
    string? ApplicationVersion, bool IsActive, string? CertificateThumbprint, string? IssuedIdentityId,
    string? BuildHash, string? ConfigHash, string? VerifiedSerialNumber, string? SerialEvidenceSource,
    DateTimeOffset? SerialEvidenceVerifiedUtc, string RegistrationSource, bool IsHidden, bool IsDefault,
    DateTimeOffset? ArchivedUtc, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

internal sealed record RecoveryAccountDeviceLink(
    string AccountId, string DeviceId, string Relationship, DateTimeOffset CreatedUtc);

internal sealed record RecoveryDeviceMapping(
    string DeviceId, string MappingKey, string MappingValue, DateTimeOffset UpdatedUtc);

internal sealed record RecoverySourceData(
    IReadOnlyList<RecoveryDevice> Devices,
    IReadOnlyList<RecoveryAccountDeviceLink> AccountDeviceLinks,
    IReadOnlyList<RecoveryDeviceMapping> DeviceMappings);

internal sealed record RecoveryPlan(
    int SourceDeviceCount,
    IReadOnlyList<RecoveryDevice> EligibleDevices,
    int ExcludedSyntheticDevices,
    int AlreadyPresentDevices,
    IReadOnlyList<RecoveryDevice> DevicesToInsert,
    IReadOnlyList<RecoveryAccountDeviceLink> SourceAccountDeviceLinks,
    IReadOnlyList<RecoveryAccountDeviceLink> AccountDeviceLinksToInsert,
    int LinksMissingTargetAccounts,
    IReadOnlyList<RecoveryDeviceMapping> SourceDeviceHostMappings,
    IReadOnlyList<RecoveryDeviceMapping> DeviceHostMappingsToInsert);

internal sealed record RecoveryReport(
    int SourceDevices,
    int EligibleDevices,
    int ExcludedSyntheticDevices,
    int AlreadyPresentDevices,
    int DevicesToInsert,
    int SourceAccountDeviceLinks,
    int AccountLinksToInsert,
    int LinksMissingTargetAccounts,
    int SourceDeviceHostMappings,
    int MappingsToInsert,
    bool Applied,
    int InsertedDevices,
    int InsertedAccountDeviceLinks,
    int InsertedDeviceHostMappings,
    bool RevisionBumped);