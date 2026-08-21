using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed partial class PostgreSqlCloudStateStore
{
    public RobotMergeResult MergeRobotRecords(string sourceDeviceId, string targetDeviceId)
    {
        if (string.IsNullOrWhiteSpace(sourceDeviceId) || string.IsNullOrWhiteSpace(targetDeviceId) ||
            sourceDeviceId.Equals(targetDeviceId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose two different robot records.");
        if (sourceDeviceId.Equals(GetRobot().DeviceId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The active robot record must be the canonical target, not the merge source.");
        var source = Sync(_devices.GetByDeviceIdAsync(sourceDeviceId)) ??
                     throw new KeyNotFoundException("Source robot record was not found.");
        var target = Sync(_devices.GetByDeviceIdAsync(targetDeviceId)) ??
                     throw new KeyNotFoundException("Target robot record was not found.");

        var migratedSessions = 0;
        foreach (var session in _sessions.Values.Where(item =>
                     source.DeviceId.Equals(item.DeviceId, StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrWhiteSpace(session.Token) &&
                !session.Token.StartsWith("conn:", StringComparison.OrdinalIgnoreCase))
                session.DeviceId = target.DeviceId;
            ApplyRegisteredDeviceMetadata(session, target);
            migratedSessions++;
        }

        var migratedBindings = Sync(_devices.MoveCredentialBindingsAsync(source.DeviceId, target.DeviceId,
            "robot-merge"));
        var mappings = new Dictionary<string, string>(source.HostMappings, StringComparer.OrdinalIgnoreCase)
        {
            ["openjibo.mergedIntoDeviceId"] = target.DeviceId
        };
        Sync(_devices.UpsertAsync(CopyDeviceIdentityState(source, false, true, DateTimeOffset.UtcNow, mappings),
            GetAccount().AccountId));
        Sync(_identityLinks.UpsertAsync(source.DeviceId, target.DeviceId, "robot-merge"));
        return new RobotMergeResult(source.DeviceId, target.DeviceId, migratedSessions, migratedBindings,
            DateTimeOffset.UtcNow);
    }

    public RobotIdentityCleanupPreview PreviewRobotIdentityCleanup()
    {
        var account = GetAccount();
        var relationships = GetDevices()
            .Where(device => device.HostMappings.TryGetValue("openjibo.mergedIntoDeviceId", out var target) &&
                             !string.IsNullOrWhiteSpace(target))
            .Select(device => new RobotMergeRelationship(device.DeviceId,
                device.HostMappings["openjibo.mergedIntoDeviceId"]))
            .OrderBy(item => item.SourceDeviceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sessionBindings = _sessions.Values.Count(session =>
            session.Metadata.TryGetValue("registeredDeviceId", out var value) &&
            !string.IsNullOrWhiteSpace(value?.ToString()));
        var authSessions = _sessions.DurableTokenValues.Count;
        var bindings = Sync(_devices.ListCredentialBindingsForAccountAsync(account.AccountId)).Count;
        return new RobotIdentityCleanupPreview(relationships.Length, sessionBindings, authSessions, bindings,
            relationships);
    }

    public RobotIdentityCleanupResult ResetRobotIdentityAssociations()
    {
        var account = GetAccount();
        var restored = 0;
        foreach (var source in GetDevices().Where(device =>
                     device.HostMappings.ContainsKey("openjibo.mergedIntoDeviceId")).ToArray())
        {
            var mappings = new Dictionary<string, string>(source.HostMappings, StringComparer.OrdinalIgnoreCase);
            mappings.Remove("openjibo.mergedIntoDeviceId");
            mappings.Remove("openjibo.boundRegisteredDeviceId");
            mappings.Remove("openjibo.boundRegisteredRobotId");
            Sync(_devices.UpsertAsync(CopyDeviceIdentityState(source, true, false, null, mappings),
                account.AccountId));
            Sync(_identityLinks.RevokeAsync(source.DeviceId));
            restored++;
        }

        var cleared = 0;
        foreach (var session in _sessions.Values)
        {
            if (session.Metadata.Remove("registeredDeviceId")) cleared++;
            session.Metadata.Remove("registeredRobotId");
            session.Metadata.Remove("identitySuggestionDeviceId");
        }

        var revoked = Sync(_authTokens.RevokeForAccountAsync(account.AccountId));
        foreach (var token in _sessions.Keys.Where(IsIssuedAuthenticationToken).ToArray())
            _sessions.TryRemove(token, out _);
        var preservedBindings = Sync(_devices.ListCredentialBindingsForAccountAsync(account.AccountId)).Count;
        return new RobotIdentityCleanupResult(restored, cleared, revoked, preservedBindings, DateTimeOffset.UtcNow);
    }

    private static bool IsIssuedAuthenticationToken(string token) =>
        token.StartsWith("token-", StringComparison.OrdinalIgnoreCase) ||
        token.StartsWith("hub-", StringComparison.OrdinalIgnoreCase);

    private static DeviceRegistration CopyDeviceIdentityState(DeviceRegistration source, bool isActive,
        bool isHidden, DateTimeOffset? archivedUtc, IDictionary<string, string> hostMappings) => new()
    {
        DeviceId = source.DeviceId, RobotId = source.RobotId, FriendlyName = source.FriendlyName,
        FirmwareVersion = source.FirmwareVersion, ApplicationVersion = source.ApplicationVersion,
        IsActive = isActive, CertificateThumbprint = source.CertificateThumbprint,
        IssuedIdentityId = source.IssuedIdentityId, BuildHash = source.BuildHash, ConfigHash = source.ConfigHash,
        VerifiedSerialNumber = source.VerifiedSerialNumber, SerialEvidenceSource = source.SerialEvidenceSource,
        SerialEvidenceVerifiedUtc = source.SerialEvidenceVerifiedUtc, RegistrationSource = source.RegistrationSource,
        IsHidden = isHidden, ArchivedUtc = archivedUtc,
        HostMappings = new Dictionary<string, string>(hostMappings, StringComparer.OrdinalIgnoreCase)
    };
}
