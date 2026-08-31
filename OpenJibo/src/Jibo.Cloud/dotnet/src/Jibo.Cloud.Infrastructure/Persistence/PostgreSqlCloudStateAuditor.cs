using Npgsql;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal static class PostgreSqlCloudStateAuditor
{
    public static async Task<CloudStateAuditReport> AuditAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var existingOptions = builder.Options;
        builder.Options = string.IsNullOrWhiteSpace(existingOptions)
            ? "-c default_transaction_read_only=on"
            : $"{existingOptions} -c default_transaction_read_only=on";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        string? snapshotJson;
        await using (var snapshot = new NpgsqlCommand("""
                                                       SELECT SnapshotJson
                                                       FROM PersistenceSnapshots
                                                       WHERE SnapshotName = 'cloud-state'
                                                       """, connection))
        {
            snapshotJson = await snapshot.ExecuteScalarAsync(cancellationToken) as string;
        }

        var legacy = snapshotJson is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(
                PostgreSqlCloudStateSnapshotImporter.GetLegacyDurableFamilyCounts(snapshotJson),
                StringComparer.OrdinalIgnoreCase);
        var normalized = await LoadNormalizedCountsAsync(connection, cancellationToken);
        var delta = normalized.Keys.ToDictionary(
            key => key,
            key => normalized[key] - legacy.GetValueOrDefault(key),
            StringComparer.OrdinalIgnoreCase);
        var integrity = await LoadIntegrityCountsAsync(connection, cancellationToken);

        return new CloudStateAuditReport(snapshotJson is not null, legacy, normalized, delta, integrity);
    }

    private static async Task<IReadOnlyDictionary<string, int>> LoadNormalizedCountsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
                                                    SELECT
                                                      (SELECT COUNT(*) FROM Accounts) AS accounts,
                                                      (SELECT COUNT(*) FROM Devices) AS devices,
                                                      (SELECT COUNT(*) FROM RobotProfiles) AS "robotProfiles",
                                                      (SELECT COUNT(*) FROM RobotCredentialBindings) AS "robotCredentialBindings",
                                                      (SELECT COUNT(*) FROM CloudAuthTokens) AS "issuedTokens",
                                                      (SELECT COUNT(*) FROM RobotIdentityLinks) AS "robotIdentityLinks",
                                                      (SELECT COUNT(*) FROM LoopSymmetricKeys) AS "symmetricKeys",
                                                      (SELECT COUNT(*) FROM KeyRequests) AS "keyRequests",
                                                      (SELECT COUNT(*) FROM UpdateManifests) AS updates,
                                                      (SELECT COUNT(*) FROM MediaRecords) AS media,
                                                      (SELECT COUNT(*) FROM BackupManifests) AS backups,
                                                      (SELECT COUNT(*) FROM CommuteProfiles) AS "commuteProfiles",
                                                      (SELECT COUNT(*) FROM CalendarEvents) AS "calendarEvents",
                                                      (SELECT COUNT(*) FROM GreetingPresences) AS "greetingPresences",
                                                      (SELECT COUNT(*) FROM Loops) AS loops,
                                                      (SELECT COUNT(*) FROM HolidayOverrides) AS holidays,
                                                      (SELECT COUNT(*) FROM LoopMembers) AS "loopMembers",
                                                      (SELECT COUNT(*) FROM People) AS people,
                                                      (SELECT COUNT(*) FROM Users) AS users,
                                                      (SELECT COUNT(*) FROM RecognitionObservations) AS "recognitionObservations",
                                                      (SELECT COUNT(*) FROM RevokedIdentityGraphAnchors) AS "revokedIdentityGraphAnchors",
                                                      (SELECT COUNT(*) FROM TrustedServerAdmissions) AS "trustedServerAdmissions",
                                                      (SELECT COUNT(*) FROM TrustedServers) AS "trustedServers"
                                                    """, connection);

        return await ReadCountsAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, int>> LoadIntegrityCountsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
                                                    SELECT
                                                      (SELECT COUNT(*) FROM AccountDevices) AS "accountDeviceLinks",
                                                      (SELECT COUNT(*) FROM Devices d
                                                        WHERE NOT EXISTS (
                                                          SELECT 1 FROM AccountDevices ad WHERE ad.DeviceId = d.DeviceId
                                                        )) AS "devicesWithoutAccountLinks",
                                                      (SELECT COUNT(*) FROM Devices d
                                                        WHERE d.IsActive AND NOT d.IsHidden AND d.ArchivedUtc IS NULL
                                                          AND NOT EXISTS (
                                                            SELECT 1 FROM AccountDevices ad WHERE ad.DeviceId = d.DeviceId
                                                          )) AS "activeVisibleDevicesWithoutAccountLinks",
                                                      (SELECT COUNT(*) FROM RobotProfiles
                                                        WHERE DeviceId IS NULL) AS "robotProfilesWithoutDeviceLinks",
                                                      (SELECT COUNT(*) FROM RobotProfiles rp
                                                        JOIN Devices d ON d.DeviceId = rp.DeviceId
                                                        WHERE LOWER(rp.RobotId) <> LOWER(d.RobotId)) AS "robotProfilesWithMismatchedRobotIds",
                                                      (SELECT COUNT(*) FROM RobotIdentityLinks
                                                        WHERE RevokedUtc IS NULL) AS "activeRobotIdentityLinks",
                                                      (SELECT COUNT(*) FROM RobotIdentityLinks ril
                                                        JOIN Devices d ON d.DeviceId = ril.InventoryDeviceId
                                                        WHERE ril.RevokedUtc IS NULL
                                                          AND (NOT d.IsActive OR d.ArchivedUtc IS NOT NULL))
                                                        AS "activeIdentityLinksToInactiveDevices",
                                                      (SELECT COUNT(*) FROM (
                                                        SELECT LOWER(RobotId)
                                                        FROM Devices
                                                        WHERE IsActive AND ArchivedUtc IS NULL
                                                        GROUP BY LOWER(RobotId)
                                                        HAVING COUNT(*) > 1
                                                      ) duplicates) AS "duplicateActiveRobotIds",
                                                      (SELECT COUNT(*) FROM Accounts WHERE IsDefault) AS "defaultAccounts",
                                                      (SELECT COUNT(*) FROM Devices WHERE IsDefault) AS "defaultDevices",
                                                      (SELECT COUNT(*) FROM BackupManifests
                                                        WHERE BackupSchemaVersion = 1) AS "backupSchemaV1",
                                                      (SELECT COUNT(*) FROM BackupManifests
                                                        WHERE BackupSchemaVersion = 2) AS "backupSchemaV2",
                                                      (SELECT COUNT(*) FROM BackupManifests
                                                        WHERE BackupSchemaVersion NOT IN (1, 2)) AS "unsupportedBackupSchemas",
                                                      (SELECT COUNT(*) FROM BackupManifests
                                                        WHERE Status = 'available') AS "availableBackups"
                                                    """, connection);

        return await ReadCountsAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, int>> ReadCountsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The cloud-state audit count query returned no row.");

        for (var index = 0; index < reader.FieldCount; index++)
            counts[reader.GetName(index)] = checked((int)reader.GetInt64(index));

        return counts;
    }
}

internal sealed record CloudStateAuditReport(
    bool SnapshotPresent,
    IReadOnlyDictionary<string, int> Legacy,
    IReadOnlyDictionary<string, int> Normalized,
    IReadOnlyDictionary<string, int> Delta,
    IReadOnlyDictionary<string, int> Integrity);
