using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed partial class PostgreSqlCloudStateStore
{
    public RobotMergeResult MergeRobotRecords(string sourceDeviceId, string targetDeviceId) =>
        MergeRobotRecordsCore(sourceDeviceId, targetDeviceId, administration: false);

    public RobotMergeResult MergeRobotRecordsForAdministration(string sourceDeviceId, string targetDeviceId) =>
        MergeRobotRecordsCore(sourceDeviceId, targetDeviceId, administration: true);

    public RobotMergeResult MergeRobotRecordsForAdministration(string sourceDeviceId, string targetDeviceId,
        RobotMergePrecondition precondition)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(precondition.SessionIds);
        ArgumentNullException.ThrowIfNull(precondition.CredentialFingerprints);
        if (string.IsNullOrWhiteSpace(sourceDeviceId) || string.IsNullOrWhiteSpace(targetDeviceId) ||
            sourceDeviceId.Equals(targetDeviceId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose two different robot records.");
        if (sourceDeviceId.Equals(GetRobot().DeviceId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The active robot record must be the canonical target, not the merge source.");

        var expectedSessionIds = NormalizeValues(precondition.SessionIds);
        var previewSessions = _sessions.Values.Where(item =>
                sourceDeviceId.Equals(item.DeviceId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (!NormalizeValues(previewSessions.Select(item => item.SessionId)).SequenceEqual(expectedSessionIds,
                StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Robot merge state changed after preview. Review the merge again.");

        var source = Sync(_devices.GetByDeviceIdAsync(sourceDeviceId)) ??
                     throw new KeyNotFoundException("Source robot record was not found.");
        var target = Sync(_devices.GetByDeviceIdAsync(targetDeviceId)) ??
                     throw new KeyNotFoundException("Target robot record was not found.");
        var migratedBindings = Sync(_devices.MergeForAdministrationAsync(source.DeviceId, target.DeviceId,
            NormalizeValues(precondition.CredentialFingerprints), "robot-merge"));

        // Durable identity is committed first. Rebind every local source session observed afterward so a session
        // admitted during the transaction also converges, without blocking all socket admission on database I/O.
        var migratedSessions = _sessions.ExecuteExclusive(sessions =>
        {
            var currentSessions = sessions.Where(item =>
                    source.DeviceId.Equals(item.DeviceId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var session in currentSessions)
            {
                if (!string.IsNullOrWhiteSpace(session.Token) &&
                    !session.Token.StartsWith("conn:", StringComparison.OrdinalIgnoreCase))
                    session.DeviceId = target.DeviceId;
                ApplyRegisteredDeviceMetadata(session, target);
            }
            return currentSessions.Length;
        });
        return new RobotMergeResult(source.DeviceId, target.DeviceId, migratedSessions, migratedBindings,
            DateTimeOffset.UtcNow);
    }

    private static string[] NormalizeValues(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private RobotMergeResult MergeRobotRecordsCore(string sourceDeviceId, string targetDeviceId, bool administration)
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
        if (source.IsHidden || source.ArchivedUtc is not null || target.IsHidden || target.ArchivedUtc is not null)
            throw new InvalidOperationException("Robot merge requires two visible, unarchived records.");
        if (administration)
        {
            var sourceAccounts = Sync(_devices.ListAccountIdsAsync(source.DeviceId));
            var targetAccounts = Sync(_devices.ListAccountIdsAsync(target.DeviceId));
            if (!sourceAccounts.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(targetAccounts))
                throw new InvalidOperationException("Admin merge requires identical account associations.");
        }

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
            administration ? null : GetAccount().AccountId));
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
