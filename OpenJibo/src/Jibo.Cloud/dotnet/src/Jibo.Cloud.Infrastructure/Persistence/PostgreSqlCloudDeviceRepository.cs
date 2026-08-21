using Jibo.Cloud.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlCloudDeviceRepository : ICloudDeviceRepository
{
    private const string DeviceColumns = """
        DeviceId, RobotId, FriendlyName, FirmwareVersion, ApplicationVersion, IsActive,
        CertificateThumbprint, IssuedIdentityId, BuildHash, ConfigHash, VerifiedSerialNumber,
        SerialEvidenceSource, SerialEvidenceVerifiedUtc, RegistrationSource, IsHidden, ArchivedUtc
        """;

    private const string QualifiedDeviceColumns = """
        d.DeviceId, d.RobotId, d.FriendlyName, d.FirmwareVersion, d.ApplicationVersion, d.IsActive,
        d.CertificateThumbprint, d.IssuedIdentityId, d.BuildHash, d.ConfigHash, d.VerifiedSerialNumber,
        d.SerialEvidenceSource, d.SerialEvidenceVerifiedUtc, d.RegistrationSource, d.IsHidden, d.ArchivedUtc
        """;

    private readonly BoundedExpiringCache<string, DeviceRegistration> _cache;
    private readonly PostgreSqlCloudStateDataSource _dataSource;

    public PostgreSqlCloudDeviceRepository(PostgreSqlCloudStateDataSource dataSource, int cacheMaxEntries = 256,
        TimeSpan? cacheTtl = null, TimeProvider? timeProvider = null)
    {
        _dataSource = dataSource;
        _cache = new BoundedExpiringCache<string, DeviceRegistration>(cacheMaxEntries,
            cacheTtl ?? TimeSpan.FromMinutes(5), StringComparer.OrdinalIgnoreCase, timeProvider);
    }

    internal void ClearCache() => _cache.Clear();

    public async Task<DeviceRegistration?> GetByDeviceIdAsync(string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        var key = deviceId.Trim();
        if (_cache.TryGet(key, out var cached)) return Clone(cached);

        var device = await ReadOneAsync("LOWER(DeviceId) = LOWER(@value)", key, cancellationToken);
        if (device is not null) _cache.Set(device.DeviceId, Clone(device));
        return device;
    }

    public async Task<DeviceRegistration?> FindByFriendlyIdAsync(string friendlyId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(friendlyId)) return null;
        var value = friendlyId.Trim();
        if (_cache.TryGet(value, out var cached)) return Clone(cached);

        var device = await ReadOneAsync("""
                                        LOWER(DeviceId) = LOWER(@value) OR
                                        LOWER(RobotId) = LOWER(@value) OR
                                        LOWER(FriendlyName) = LOWER(@value)
                                        """, value, cancellationToken);
        if (device is not null) _cache.Set(device.DeviceId, Clone(device));
        return device;
    }

    public async Task<DeviceRegistration?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var device = await ReadOneWithoutValueAsync("IsDefault", cancellationToken);
        if (device is not null) _cache.Set(device.DeviceId, Clone(device));
        return device;
    }

    public async Task<IReadOnlyList<DeviceRegistration>> ListForAccountAsync(string accountId,
        bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return [];
        await using var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT {QualifiedDeviceColumns}
                               FROM Devices d
                               INNER JOIN AccountDevices ad ON ad.DeviceId = d.DeviceId
                               WHERE ad.AccountId = @accountId
                                 AND (@includeArchived OR (NOT d.IsHidden AND d.ArchivedUtc IS NULL))
                               ORDER BY d.FriendlyName, d.DeviceId
                               """;
        command.Parameters.AddWithValue("accountId", accountId.Trim());
        command.Parameters.AddWithValue("includeArchived", includeArchived);
        var devices = new List<DeviceRegistration>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) devices.Add(MapDevice(reader));

        await LoadHostMappingsAsync(connection, devices, cancellationToken);
        foreach (var device in devices) _cache.Set(device.DeviceId, Clone(device));
        return devices.Select(Clone).ToArray();
    }

    public async Task<DeviceRegistration> UpsertAsync(DeviceRegistration device, string? accountId = null,
        bool? isDefault = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrWhiteSpace(device.DeviceId))
            throw new ArgumentException("DeviceId is required.", nameof(device));
        if (string.IsNullOrWhiteSpace(device.RobotId))
            throw new ArgumentException("RobotId is required.", nameof(device));

        var deviceId = device.DeviceId.Trim();
        await using var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (isDefault == true)
        {
            await using var clearDefault = new NpgsqlCommand(
                "UPDATE Devices SET IsDefault = FALSE, UpdatedUtc = NOW() WHERE IsDefault AND DeviceId <> @id",
                connection, transaction);
            clearDefault.Parameters.AddWithValue("id", deviceId);
            await clearDefault.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand("""
                                                     INSERT INTO Devices
                                                         (DeviceId, RobotId, FriendlyName, FirmwareVersion,
                                                          ApplicationVersion, IsActive, CertificateThumbprint,
                                                          IssuedIdentityId, BuildHash, ConfigHash,
                                                          VerifiedSerialNumber, SerialEvidenceSource,
                                                          SerialEvidenceVerifiedUtc, RegistrationSource,
                                                          IsHidden, IsDefault, ArchivedUtc)
                                                     VALUES
                                                         (@deviceId, @robotId, @friendlyName, @firmwareVersion,
                                                          @applicationVersion, @isActive, @certificateThumbprint,
                                                          @issuedIdentityId, @buildHash, @configHash,
                                                          @verifiedSerialNumber, @serialEvidenceSource,
                                                          @serialEvidenceVerifiedUtc, @registrationSource,
                                                          @isHidden, @insertIsDefault, @archivedUtc)
                                                     ON CONFLICT (DeviceId) DO UPDATE SET
                                                         RobotId = EXCLUDED.RobotId,
                                                         FriendlyName = EXCLUDED.FriendlyName,
                                                         FirmwareVersion = EXCLUDED.FirmwareVersion,
                                                         ApplicationVersion = EXCLUDED.ApplicationVersion,
                                                         IsActive = EXCLUDED.IsActive,
                                                         CertificateThumbprint = EXCLUDED.CertificateThumbprint,
                                                         IssuedIdentityId = EXCLUDED.IssuedIdentityId,
                                                         BuildHash = EXCLUDED.BuildHash,
                                                         ConfigHash = EXCLUDED.ConfigHash,
                                                         VerifiedSerialNumber = EXCLUDED.VerifiedSerialNumber,
                                                         SerialEvidenceSource = EXCLUDED.SerialEvidenceSource,
                                                         SerialEvidenceVerifiedUtc = EXCLUDED.SerialEvidenceVerifiedUtc,
                                                         RegistrationSource = EXCLUDED.RegistrationSource,
                                                         IsHidden = EXCLUDED.IsHidden,
                                                         IsDefault = COALESCE(@isDefault, Devices.IsDefault),
                                                         ArchivedUtc = EXCLUDED.ArchivedUtc,
                                                         UpdatedUtc = NOW()
                                                     """, connection, transaction))
        {
            AddDeviceParameters(command, device, isDefault);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteMappings = new NpgsqlCommand(
                         "DELETE FROM DeviceHostMappings WHERE DeviceId = @deviceId", connection, transaction))
        {
            deleteMappings.Parameters.AddWithValue("deviceId", deviceId);
            await deleteMappings.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var mapping in device.HostMappings)
        {
            await using var insertMapping = new NpgsqlCommand("""
                                                              INSERT INTO DeviceHostMappings
                                                                  (DeviceId, MappingKey, MappingValue)
                                                              VALUES (@deviceId, @key, @value)
                                                              """, connection, transaction);
            insertMapping.Parameters.AddWithValue("deviceId", deviceId);
            insertMapping.Parameters.AddWithValue("key", mapping.Key);
            insertMapping.Parameters.AddWithValue("value", mapping.Value);
            await insertMapping.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(accountId))
        {
            await using var accountDevice = new NpgsqlCommand("""
                                                              INSERT INTO AccountDevices (AccountId, DeviceId)
                                                              VALUES (@accountId, @deviceId)
                                                              ON CONFLICT (AccountId, DeviceId) DO NOTHING
                                                              """, connection, transaction);
            accountDevice.Parameters.AddWithValue("accountId", accountId.Trim());
            accountDevice.Parameters.AddWithValue("deviceId", deviceId);
            await accountDevice.ExecuteNonQueryAsync(cancellationToken);
        }

        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _cache.Remove(deviceId);
        _cache.Set(deviceId, Clone(device));
        return Clone(device);
    }

    public async Task<RobotCredentialBinding?> GetCredentialBindingAsync(string accessKeyFingerprint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessKeyFingerprint)) return null;
        await using var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT AccessKeyFingerprint, DeviceId, ClaimedUtc, ClaimSource
                              FROM RobotCredentialBindings
                              WHERE AccessKeyFingerprint = @fingerprint
                              """;
        command.Parameters.AddWithValue("fingerprint", accessKeyFingerprint.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? MapBinding(reader)
            : null;
    }

    public async Task<IReadOnlyList<RobotCredentialBinding>> ListCredentialBindingsAsync(string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return [];
        await using var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT AccessKeyFingerprint, DeviceId, ClaimedUtc, ClaimSource
                              FROM RobotCredentialBindings
                              WHERE LOWER(DeviceId) = LOWER(@deviceId)
                              ORDER BY ClaimedUtc DESC
                              """;
        command.Parameters.AddWithValue("deviceId", deviceId.Trim());
        var result = new List<RobotCredentialBinding>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(MapBinding(reader));
        return result;
    }

    public async Task<IReadOnlyList<RobotCredentialBinding>> ListCredentialBindingsForAccountAsync(string accountId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return [];
        await using var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT DISTINCT r.AccessKeyFingerprint, r.DeviceId, r.ClaimedUtc, r.ClaimSource
                              FROM RobotCredentialBindings r
                              INNER JOIN AccountDevices ad ON ad.DeviceId = r.DeviceId
                              WHERE ad.AccountId = @accountId
                              ORDER BY r.ClaimedUtc DESC
                              """;
        command.Parameters.AddWithValue("accountId", accountId.Trim());
        var result = new List<RobotCredentialBinding>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(MapBinding(reader));
        return result;
    }

    public async Task<RobotCredentialBinding> BindCredentialAsync(string deviceId, string accessKeyFingerprint,
        string claimSource, CancellationToken cancellationToken = default)
    {
        var device = await GetByDeviceIdAsync(deviceId, cancellationToken)
                     ?? throw new KeyNotFoundException("Robot record was not found.");
        if (string.IsNullOrWhiteSpace(accessKeyFingerprint))
            throw new ArgumentException("Credential fingerprint is required.", nameof(accessKeyFingerprint));

        var fingerprint = accessKeyFingerprint.Trim();
        await using var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand("""
                                                    INSERT INTO RobotCredentialBindings
                                                        (AccessKeyFingerprint, DeviceId, ClaimedUtc, ClaimSource)
                                                    VALUES (@fingerprint, @deviceId, NOW(), @claimSource)
                                                    ON CONFLICT (AccessKeyFingerprint) DO NOTHING
                                                    """, connection, transaction))
        {
            insert.Parameters.AddWithValue("fingerprint", fingerprint);
            insert.Parameters.AddWithValue("deviceId", device.DeviceId);
            insert.Parameters.AddWithValue("claimSource",
                string.IsNullOrWhiteSpace(claimSource) ? "admin-claim" : claimSource.Trim());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        RobotCredentialBinding binding;
        await using (var read = new NpgsqlCommand("""
                                                  SELECT AccessKeyFingerprint, DeviceId, ClaimedUtc, ClaimSource
                                                  FROM RobotCredentialBindings
                                                  WHERE AccessKeyFingerprint = @fingerprint
                                                  FOR UPDATE
                                                  """, connection, transaction))
        {
            read.Parameters.AddWithValue("fingerprint", fingerprint);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Credential binding could not be created.");
            binding = MapBinding(reader);
        }

        if (!string.Equals(binding.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Credential fingerprint is already claimed by another robot.");

        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return binding;
    }

    public async Task<DeviceRegistration?> FindByCredentialFingerprintAsync(string accessKeyFingerprint,
        CancellationToken cancellationToken = default)
    {
        var binding = await GetCredentialBindingAsync(accessKeyFingerprint, cancellationToken);
        return binding is null ? null : await GetByDeviceIdAsync(binding.DeviceId, cancellationToken);
    }

    public async Task<IReadOnlyList<RobotCredentialBinding>> SwapCredentialBindingsAsync(
        string firstAccessKeyFingerprint, string secondAccessKeyFingerprint, string claimSource,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(firstAccessKeyFingerprint) ||
            string.IsNullOrWhiteSpace(secondAccessKeyFingerprint) ||
            firstAccessKeyFingerprint.Equals(secondAccessKeyFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose two different credential fingerprints.");

        await using var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fingerprints = new[] { firstAccessKeyFingerprint.Trim(), secondAccessKeyFingerprint.Trim() };
        var bindings = new Dictionary<string, RobotCredentialBinding>(StringComparer.OrdinalIgnoreCase);
        await using (var read = new NpgsqlCommand("""
                                                  SELECT AccessKeyFingerprint, DeviceId, ClaimedUtc, ClaimSource
                                                  FROM RobotCredentialBindings
                                                  WHERE AccessKeyFingerprint = ANY(@fingerprints)
                                                  FOR UPDATE
                                                  """, connection, transaction))
        {
            read.Parameters.AddWithValue("fingerprints", fingerprints);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var binding = MapBinding(reader);
                bindings[binding.AccessKeyFingerprint] = binding;
            }
        }

        if (!bindings.TryGetValue(fingerprints[0], out var first) ||
            !bindings.TryGetValue(fingerprints[1], out var second))
            throw new KeyNotFoundException("Both credential bindings must exist before they can be swapped.");

        var now = DateTimeOffset.UtcNow;
        var source = string.IsNullOrWhiteSpace(claimSource) ? "portal-admin-swap" : claimSource.Trim();
        await using (var update = new NpgsqlCommand("""
                                                    UPDATE RobotCredentialBindings
                                                    SET DeviceId = CASE
                                                            WHEN AccessKeyFingerprint = @first THEN @secondDevice
                                                            ELSE @firstDevice END,
                                                        ClaimedUtc = @now,
                                                        ClaimSource = @source
                                                    WHERE AccessKeyFingerprint = ANY(@fingerprints)
                                                    """, connection, transaction))
        {
            update.Parameters.AddWithValue("first", fingerprints[0]);
            update.Parameters.AddWithValue("firstDevice", first.DeviceId);
            update.Parameters.AddWithValue("secondDevice", second.DeviceId);
            update.Parameters.AddWithValue("now", now);
            update.Parameters.AddWithValue("source", source);
            update.Parameters.AddWithValue("fingerprints", fingerprints);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return
        [
            new RobotCredentialBinding(first.AccessKeyFingerprint, second.DeviceId, now, source),
            new RobotCredentialBinding(second.AccessKeyFingerprint, first.DeviceId, now, source)
        ];
    }

    public async Task<int> MoveCredentialBindingsAsync(string sourceDeviceId, string targetDeviceId,
        string claimSource, CancellationToken cancellationToken = default)
    {
        var source = await GetByDeviceIdAsync(sourceDeviceId, cancellationToken) ??
                     throw new KeyNotFoundException("Source robot record was not found.");
        var target = await GetByDeviceIdAsync(targetDeviceId, cancellationToken) ??
                     throw new KeyNotFoundException("Target robot record was not found.");
        await using var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
                                                    UPDATE RobotCredentialBindings
                                                    SET DeviceId = @target, ClaimedUtc = NOW(), ClaimSource = @source
                                                    WHERE DeviceId = @sourceDevice
                                                    """, connection, transaction);
        command.Parameters.AddWithValue("target", target.DeviceId);
        command.Parameters.AddWithValue("sourceDevice", source.DeviceId);
        command.Parameters.AddWithValue("source",
            string.IsNullOrWhiteSpace(claimSource) ? "robot-merge" : claimSource.Trim());
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected > 0) await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return affected;
    }

    private async Task<DeviceRegistration?> ReadOneAsync(string predicate, string value,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {DeviceColumns} FROM Devices WHERE {predicate} ORDER BY CreatedUtc LIMIT 1";
        command.Parameters.AddWithValue("value", value);
        DeviceRegistration? device;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            device = await reader.ReadAsync(cancellationToken) ? MapDevice(reader) : null;
        if (device is not null) await LoadHostMappingsAsync(connection, [device], cancellationToken);
        return device;
    }

    private async Task<DeviceRegistration?> ReadOneWithoutValueAsync(string predicate,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {DeviceColumns} FROM Devices WHERE {predicate} ORDER BY CreatedUtc LIMIT 1";
        DeviceRegistration? device;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            device = await reader.ReadAsync(cancellationToken) ? MapDevice(reader) : null;
        if (device is not null) await LoadHostMappingsAsync(connection, [device], cancellationToken);
        return device;
    }

    private static async Task LoadHostMappingsAsync(NpgsqlConnection connection,
        IReadOnlyCollection<DeviceRegistration> devices, CancellationToken cancellationToken)
    {
        if (devices.Count == 0) return;
        var byId = devices.ToDictionary(device => device.DeviceId, StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT DeviceId, MappingKey, MappingValue
                              FROM DeviceHostMappings
                              WHERE DeviceId = ANY(@deviceIds)
                              ORDER BY DeviceId, MappingKey
                              """;
        command.Parameters.AddWithValue("deviceIds", byId.Keys.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (byId.TryGetValue(reader.GetString(0), out var device))
                device.HostMappings[reader.GetString(1)] = reader.GetString(2);
    }

    private static DeviceRegistration MapDevice(NpgsqlDataReader reader) => new()
    {
        DeviceId = reader.GetString(0),
        RobotId = reader.GetString(1),
        FriendlyName = reader.GetString(2),
        FirmwareVersion = reader.IsDBNull(3) ? null : reader.GetString(3),
        ApplicationVersion = reader.IsDBNull(4) ? null : reader.GetString(4),
        IsActive = reader.GetBoolean(5),
        CertificateThumbprint = reader.IsDBNull(6) ? null : reader.GetString(6),
        IssuedIdentityId = reader.IsDBNull(7) ? null : reader.GetString(7),
        BuildHash = reader.IsDBNull(8) ? null : reader.GetString(8),
        ConfigHash = reader.IsDBNull(9) ? null : reader.GetString(9),
        VerifiedSerialNumber = reader.IsDBNull(10) ? null : reader.GetString(10),
        SerialEvidenceSource = reader.IsDBNull(11) ? null : reader.GetString(11),
        SerialEvidenceVerifiedUtc = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
        RegistrationSource = reader.GetString(13),
        IsHidden = reader.GetBoolean(14),
        ArchivedUtc = reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
        HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };

    private static RobotCredentialBinding MapBinding(NpgsqlDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3));

    private static void AddDeviceParameters(NpgsqlCommand command, DeviceRegistration device, bool? isDefault)
    {
        command.Parameters.AddWithValue("deviceId", device.DeviceId.Trim());
        command.Parameters.AddWithValue("robotId", device.RobotId.Trim());
        command.Parameters.AddWithValue("friendlyName", device.FriendlyName.Trim());
        NpgsqlParameterHelpers.AddNullable(command.Parameters, "firmwareVersion", NpgsqlDbType.Text,
            device.FirmwareVersion);
        NpgsqlParameterHelpers.AddNullable(command.Parameters, "applicationVersion", NpgsqlDbType.Text,
            device.ApplicationVersion);
        command.Parameters.AddWithValue("isActive", device.IsActive);
        NpgsqlParameterHelpers.AddNullable(command.Parameters, "certificateThumbprint", NpgsqlDbType.Text,
            device.CertificateThumbprint);
        NpgsqlParameterHelpers.AddNullable(command.Parameters, "issuedIdentityId", NpgsqlDbType.Text,
            device.IssuedIdentityId);
        NpgsqlParameterHelpers.AddNullable(command.Parameters, "buildHash", NpgsqlDbType.Text, device.BuildHash);
        NpgsqlParameterHelpers.AddNullable(command.Parameters, "configHash", NpgsqlDbType.Text, device.ConfigHash);
        NpgsqlParameterHelpers.AddNullable(command.Parameters, "verifiedSerialNumber", NpgsqlDbType.Text,
            device.VerifiedSerialNumber);
        NpgsqlParameterHelpers.AddNullable(command.Parameters, "serialEvidenceSource", NpgsqlDbType.Text,
            device.SerialEvidenceSource);
        NpgsqlParameterHelpers.AddNullable(command.Parameters, "serialEvidenceVerifiedUtc",
            NpgsqlDbType.TimestampTz, device.SerialEvidenceVerifiedUtc);
        command.Parameters.AddWithValue("registrationSource",
            RobotRegistrationSources.Normalize(device.RegistrationSource, device.DeviceId));
        command.Parameters.AddWithValue("isHidden", device.IsHidden);
        command.Parameters.AddWithValue("insertIsDefault", isDefault ?? false);
        command.Parameters.Add("isDefault", NpgsqlDbType.Boolean).Value =
            (object?)isDefault ?? DBNull.Value;
        NpgsqlParameterHelpers.AddNullable(command.Parameters, "archivedUtc", NpgsqlDbType.TimestampTz,
            device.ArchivedUtc);
    }

    private static DeviceRegistration Clone(DeviceRegistration device) => new()
    {
        DeviceId = device.DeviceId,
        RobotId = device.RobotId,
        FriendlyName = device.FriendlyName,
        FirmwareVersion = device.FirmwareVersion,
        ApplicationVersion = device.ApplicationVersion,
        IsActive = device.IsActive,
        CertificateThumbprint = device.CertificateThumbprint,
        IssuedIdentityId = device.IssuedIdentityId,
        BuildHash = device.BuildHash,
        ConfigHash = device.ConfigHash,
        VerifiedSerialNumber = device.VerifiedSerialNumber,
        SerialEvidenceSource = device.SerialEvidenceSource,
        SerialEvidenceVerifiedUtc = device.SerialEvidenceVerifiedUtc,
        RegistrationSource = device.RegistrationSource,
        IsHidden = device.IsHidden,
        ArchivedUtc = device.ArchivedUtc,
        HostMappings = new Dictionary<string, string>(device.HostMappings, StringComparer.OrdinalIgnoreCase)
    };
}
