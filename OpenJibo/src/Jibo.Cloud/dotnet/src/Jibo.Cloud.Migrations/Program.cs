using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Infrastructure.Media;
using Jibo.Cloud.Infrastructure.Persistence;
using Npgsql;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

var options = MigrationOptions.Parse(args);
var minimumLevel = ParseLogEventLevel(Environment.GetEnvironmentVariable("OPENJIBO_MIGRATIONS_LOG_LEVEL") ?? "Debug");
var logDirectory = ResolvePath("captures/logs");
Directory.CreateDirectory(logDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(minimumLevel)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        theme: AnsiConsoleTheme.Code,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(logDirectory, "migrations-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        restrictedToMinimumLevel: minimumLevel,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    if (options.ShowHelp)
    {
        Log.Information("{HelpText}", MigrationOptions.HelpText);
        return 0;
    }

    if (options.AuditCloudState)
    {
        var connectionString = options.ResolveConnectionString(MigrationTarget.State);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Error("No state connection string was provided for the cloud-state audit.");
            return 1;
        }

        var report = await PostgreSqlCloudStateAuditor.AuditAsync(connectionString);
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return 0;
    }
    if (options.RecoverMissingDevices)
        return await RecoverMissingDevicesAsync(options);

    MigrationTarget[] targets = options.Target switch
    {
        MigrationTarget.All => [MigrationTarget.State, MigrationTarget.PersonalMemory],
        _ => [options.Target]
    };

    var scriptsDirectory = ResolveScriptsDirectory(options.ScriptsDirectory);
    var scripts = Directory.EnumerateFiles(scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (scripts.Length == 0)
    {
        Log.Error("No PostgreSQL migration scripts found in '{ScriptsDirectory}'.", scriptsDirectory);
        return 1;
    }

    foreach (var target in targets)
    {
        var targetScripts = scripts.Where(path => AppliesToTarget(path, target)).ToArray();
        var connectionString = options.ResolveConnectionString(target);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Error("No connection string was provided for target '{Target}'.", target);
            return 1;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await EnsureTrackingTableAsync(connection);

        var applied = await LoadAppliedScriptsAsync(connection);

        foreach (var scriptPath in targetScripts)
        {
            var scriptName = Path.GetFileName(scriptPath);
            var scriptText = await File.ReadAllTextAsync(scriptPath);
            var checksum = ComputeChecksum(scriptText);

            if (applied.TryGetValue(scriptName, out var knownChecksum))
            {
                if (!string.Equals(knownChecksum, checksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Migration script '{scriptName}' has changed after being applied to '{target}'.");

                if (options.Verbose) Log.Debug("[{Target}] already applied: {ScriptName}", target, scriptName);
                continue;
            }

            if (options.PreviewOnly)
            {
                Log.Information("[{Target}] would apply: {ScriptName}", target, scriptName);
                continue;
            }

            Log.Information("[{Target}] applying: {ScriptName}", target, scriptName);
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(scriptText, connection, transaction))
            {
                command.CommandTimeout = 0;
                await command.ExecuteNonQueryAsync();
            }

            await using (var insert = new NpgsqlCommand("""
                                                        INSERT INTO OpenJiboSchemaMigrations (ScriptName, Checksum, AppliedUtc)
                                                        VALUES (@scriptName, @checksum, NOW())
                                                        ON CONFLICT (ScriptName) DO UPDATE SET
                                                            Checksum = EXCLUDED.Checksum,
                                                            AppliedUtc = EXCLUDED.AppliedUtc
                                                        """, connection, transaction))
            {
                insert.Parameters.AddWithValue("@scriptName", scriptName);
                insert.Parameters.AddWithValue("@checksum", checksum);
                await insert.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        if (target == MigrationTarget.State && options.ImportLegacyCloudState)
        {
            if (options.PreviewOnly)
            {
                Log.Information("[State] would explicitly import the legacy cloud-state snapshot.");
            }
            else if (await LoadSnapshotAsync(connection, "cloud-state") is null)
            {
                Log.Information("[State] no legacy cloud-state snapshot exists; explicit import was skipped.");
            }
            else
            {
                await using var dataSource = NpgsqlDataSource.Create(connectionString);
                var backupExportDirectory =
                    Environment.GetEnvironmentVariable("OPENJIBO_LEGACY_BACKUP_EXPORT_DIRECTORY");
                IBackupPayloadStore? backupPayloadStore = null;
                if (!string.IsNullOrWhiteSpace(options.MediaConnectionString))
                    backupPayloadStore = new MediaContentBackupPayloadStore(
                        new AzureBlobMediaContentStore(
                            options.MediaConnectionString, options.MediaContainerName));
                else if (!string.IsNullOrWhiteSpace(backupExportDirectory))
                    backupPayloadStore = new DirectoryBackupPayloadStore(backupExportDirectory);
                var importer = new PostgreSqlCloudStateSnapshotImporter(
                    dataSource,
                    new UserDataCloudStateSecretProtector(new UserDataEncryptionService()),
                    backupPayloadStore);
                var result = await importer.ImportAsync();
                Log.Information(
                    "[State] legacy import {ImportName}: sha256={SourceSha256}, alreadyImported={AlreadyImported}, counts={ImportedCounts}",
                    result.ImportName, result.SourceSha256, result.AlreadyImported, result.ImportedCounts);
            }
        }

        if (target == MigrationTarget.PersonalMemory && options.ImportLegacyPersonalMemory)
        {
            if (options.PreviewOnly)
            {
                Log.Information("[PersonalMemory] would explicitly import the legacy personal-memory snapshot.");
            }
            else
            {
                using var store = new PostgreSqlPersonalMemoryStore(connectionString);
                var state = store.GetPersistenceStateInfo();
                Log.Information(
                    "[PersonalMemory] legacy import complete: schemaVersion={SchemaVersion}, revision={Revision}",
                    state.SchemaVersion, state.Revision);
            }
        }

        if (options.Verify && !options.PreviewOnly)
            await VerifyTargetAsync(connection, target);
    }

    return 0;
}
finally
{
    Log.CloseAndFlush();
}

static string ResolveScriptsDirectory(string? configured)
{
    var candidate = string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql")
        : configured;

    return Path.GetFullPath(candidate);
}

static string ResolvePath(string configuredPath)
{
    if (Path.IsPathRooted(configuredPath)) return Path.GetFullPath(configuredPath);

    var repoRoot = FindOpenJiboRepoRoot(Directory.GetCurrentDirectory()) ??
                   FindOpenJiboRepoRoot(AppContext.BaseDirectory) ??
                   Directory.GetCurrentDirectory();

    return Path.GetFullPath(configuredPath, repoRoot);
}

static string? FindOpenJiboRepoRoot(string? startPath)
{
    if (string.IsNullOrWhiteSpace(startPath)) return null;

    var directory = new DirectoryInfo(Path.GetFullPath(startPath));
    if (directory is { Exists: false, Parent: not null }) directory = directory.Parent;

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "OpenJibo.slnx"))) return directory.FullName;

        directory = directory.Parent;
    }

    return null;
}

static LogEventLevel ParseLogEventLevel(string? value)
{
    return Enum.TryParse<LogEventLevel>(value, true, out var level)
        ? level
        : LogEventLevel.Debug;
}

static string ComputeChecksum(string value)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static async Task EnsureTrackingTableAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand("""
                                                CREATE TABLE IF NOT EXISTS OpenJiboSchemaMigrations (
                                                    ScriptName TEXT NOT NULL PRIMARY KEY,
                                                    Checksum TEXT NOT NULL,
                                                    AppliedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
                                                )
                                                """, connection);
    await command.ExecuteNonQueryAsync();
}

static async Task<Dictionary<string, string>> LoadAppliedScriptsAsync(NpgsqlConnection connection)
{
    var applied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    await using var command = new NpgsqlCommand("""
                                                SELECT ScriptName, Checksum
                                                FROM OpenJiboSchemaMigrations
                                                ORDER BY ScriptName
                                                """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var scriptName = reader.GetString(0);
        var checksum = reader.GetString(1);
        applied[scriptName] = checksum;
    }

    return applied;
}

static async Task VerifyTargetAsync(NpgsqlConnection connection, MigrationTarget target)
{
    if (target == MigrationTarget.State)
    {
        var snapshot = await LoadSnapshotAsync(connection, "cloud-state");
        if (snapshot is not null)
        {
            var sha256 = ComputeChecksum(snapshot);
            await using var import = new NpgsqlCommand("""
                                                       SELECT COUNT(*)
                                                       FROM CloudStateImports
                                                       WHERE SourceSnapshotName = 'cloud-state'
                                                         AND SourceSha256 = @sha256
                                                       """, connection);
            import.Parameters.AddWithValue("sha256", sha256);
            var importCount = (long)(await import.ExecuteScalarAsync() ?? 0L);
            if (importCount != 1)
                throw new InvalidOperationException(
                    "State verification failed: the current legacy cloud-state snapshot has not been imported.");

            await using var accounts = new NpgsqlCommand("SELECT COUNT(*) FROM Accounts", connection);
            if ((long)(await accounts.ExecuteScalarAsync() ?? 0L) == 0)
                throw new InvalidOperationException(
                    "State verification failed: the imported database has no normalized account.");
        }

        Log.Information("[State] normalized persistence verification passed.");
        return;
    }

    var personalMemorySnapshot = await LoadSnapshotAsync(connection, "personal-memory");
    if (personalMemorySnapshot is not null)
    {
        await using var import = new NpgsqlCommand("""
                                                   SELECT COUNT(*)
                                                   FROM PersonalMemoryImports
                                                   WHERE ImportName = 'persistence-snapshot-v1'
                                                   """, connection);
        var importCount = (long)(await import.ExecuteScalarAsync() ?? 0L);
        if (importCount != 1)
            throw new InvalidOperationException(
                "Personal-memory verification failed: the legacy snapshot has not been imported.");
    }

    Log.Information("[PersonalMemory] normalized persistence verification passed.");
}

static async Task<string?> LoadSnapshotAsync(NpgsqlConnection connection, string snapshotName)
{
    await using var command = new NpgsqlCommand("""
                                                SELECT SnapshotJson
                                                FROM PersistenceSnapshots
                                                WHERE SnapshotName = @snapshotName
                                                """, connection);
    command.Parameters.AddWithValue("snapshotName", snapshotName);
    return await command.ExecuteScalarAsync() as string;
}

static async Task<int> RecoverMissingDevicesAsync(MigrationOptions options)
{
    if (string.IsNullOrWhiteSpace(options.SourceStateConnectionString) ||
        string.IsNullOrWhiteSpace(options.TargetStateConnectionString))
    {
        Log.Error("Recovery requires both --source-state-connection and --target-state-connection.");
        return 1;
    }

    if (options.ApplyRequested && !options.RecoveryConfirmation)
    {
        Log.Error("Recovery mutations require --apply and --confirm-recover-missing-devices.");
        return 1;
    }

    var source = await LoadRecoverySourceAsync(options.SourceStateConnectionString);
    var report = await ExecuteRecoveryAsync(options.TargetStateConnectionString, source,
        options.ApplyRequested);
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));
    return 0;
}

static async Task<RecoverySourceData> LoadRecoverySourceAsync(string connectionString)
{
    var builder = new NpgsqlConnectionStringBuilder(connectionString);
    var existingOptions = builder.Options;
    builder.Options = string.IsNullOrWhiteSpace(existingOptions)
        ? "-c default_transaction_read_only=on"
        : $"{existingOptions} -c default_transaction_read_only=on";
    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(
        System.Data.IsolationLevel.RepeatableRead);
    await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
        await readOnly.ExecuteNonQueryAsync();

    var allDevices = new List<RecoveryDevice>();
    await using (var command = new NpgsqlCommand("""
                                                  SELECT DeviceId, RobotId, FriendlyName, FirmwareVersion,
                                                         ApplicationVersion, IsActive, CertificateThumbprint,
                                                         IssuedIdentityId, BuildHash, ConfigHash,
                                                         VerifiedSerialNumber, SerialEvidenceSource,
                                                         SerialEvidenceVerifiedUtc, RegistrationSource,
                                                         IsHidden, IsDefault, ArchivedUtc, CreatedUtc, UpdatedUtc
                                                  FROM Devices
                                                  """, connection, transaction))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            allDevices.Add(new RecoveryDevice(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), NullableString(reader, 3),
                NullableString(reader, 4), reader.GetBoolean(5), NullableString(reader, 6),
                NullableString(reader, 7), NullableString(reader, 8), NullableString(reader, 9),
                NullableString(reader, 10), NullableString(reader, 11), NullableDate(reader, 12),
                reader.GetString(13), reader.GetBoolean(14), reader.GetBoolean(15), NullableDate(reader, 16),
                reader.GetFieldValue<DateTimeOffset>(17), reader.GetFieldValue<DateTimeOffset>(18)));
        }
    }


    var links = new List<RecoveryAccountDeviceLink>();
    var mappings = new List<RecoveryDeviceMapping>();
    var ids = allDevices.Select(device => device.DeviceId).ToArray();
    if (ids.Length > 0)
    {
        await using var linksCommand = new NpgsqlCommand("""
                                                         SELECT AccountId, DeviceId, Relationship, CreatedUtc
                                                         FROM AccountDevices
                                                         WHERE DeviceId = ANY(@deviceIds)
                                                         """, connection, transaction);
        linksCommand.Parameters.AddWithValue("deviceIds", ids);
        await using (var reader = await linksCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                links.Add(new RecoveryAccountDeviceLink(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        }

        await using var mappingsCommand = new NpgsqlCommand("""
                                                            SELECT DeviceId, MappingKey, MappingValue, UpdatedUtc
                                                            FROM DeviceHostMappings
                                                            WHERE DeviceId = ANY(@deviceIds)
                                                            """, connection, transaction);
        mappingsCommand.Parameters.AddWithValue("deviceIds", ids);
        await using var mappingsReader = await mappingsCommand.ExecuteReaderAsync();
        while (await mappingsReader.ReadAsync())
            mappings.Add(new RecoveryDeviceMapping(mappingsReader.GetString(0), mappingsReader.GetString(1),
                mappingsReader.GetString(2), mappingsReader.GetFieldValue<DateTimeOffset>(3)));
    }

    await transaction.CommitAsync();
    return new RecoverySourceData(allDevices, links, mappings);
}

static async Task<RecoveryReport> ExecuteRecoveryAsync(
    string connectionString, RecoverySourceData source, bool apply)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(
        System.Data.IsolationLevel.Serializable);
    if (apply)
    {
        await using var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('openjibo:recover-missing-devices'))",
            connection, transaction);
        await lockCommand.ExecuteNonQueryAsync();
    }
    else
    {
        await using var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction);
        await readOnly.ExecuteNonQueryAsync();
    }

    var sourceIds = source.Devices.Select(device => device.DeviceId).ToArray();
    var existingDevices = await ReadExistingDeviceIdsAsync(connection, transaction, sourceIds);
    var sourceLinks = source.AccountDeviceLinks;
    var sourceMappings = source.DeviceMappings;
    var accounts = await ReadExistingAccountIdsAsync(connection, transaction,
        sourceLinks.Select(link => link.AccountId));
    var existingLinks = await ReadExistingLinksAsync(connection, transaction, sourceLinks);
    var existingMappings = await ReadExistingMappingsAsync(connection, transaction, sourceMappings);
    var plan = RecoveryPlanner.Build(source.Devices, sourceLinks, sourceMappings, existingDevices,
        accounts, existingLinks, existingMappings);
    var insertedDevices = 0;
    var insertedLinks = 0;
    var insertedMappings = 0;
    var revisionBumped = false;
    if (apply)
    {
        foreach (var device in plan.DevicesToInsert)
        {
            await using var command = new NpgsqlCommand("""
                                                         INSERT INTO Devices
                                                           (DeviceId, RobotId, FriendlyName, FirmwareVersion,
                                                            ApplicationVersion, IsActive, CertificateThumbprint,
                                                            IssuedIdentityId, BuildHash, ConfigHash,
                                                            VerifiedSerialNumber, SerialEvidenceSource,
                                                            SerialEvidenceVerifiedUtc, RegistrationSource,
                                                            IsHidden, IsDefault, ArchivedUtc, CreatedUtc, UpdatedUtc)
                                                         VALUES (@deviceId,@robotId,@friendly,@firmware,@application,@active,
                                                                 @certificate,@identity,@build,@config,@serial,@serialSource,
                                                                 @serialUtc,@registration,@hidden,FALSE,@archived,@created,@updated)
                                                         ON CONFLICT DO NOTHING
                                                         """, connection, transaction);
            AddRecoveryDeviceParameters(command, device);
            insertedDevices += await command.ExecuteNonQueryAsync();
        }

        foreach (var link in plan.AccountDeviceLinksToInsert)
        {
            await using var command = new NpgsqlCommand("""
                                                         INSERT INTO AccountDevices(AccountId,DeviceId,Relationship,CreatedUtc)
                                                         VALUES (@account,@device,@relationship,@created)
                                                         ON CONFLICT DO NOTHING
                                                         """, connection, transaction);
            command.Parameters.AddWithValue("account", link.AccountId);
            command.Parameters.AddWithValue("device", link.DeviceId);
            command.Parameters.AddWithValue("relationship", link.Relationship);
            command.Parameters.AddWithValue("created", link.CreatedUtc);
            insertedLinks += await command.ExecuteNonQueryAsync();
        }

        foreach (var mapping in plan.DeviceHostMappingsToInsert)
        {
            await using var command = new NpgsqlCommand("""
                                                         INSERT INTO DeviceHostMappings(DeviceId,MappingKey,MappingValue,UpdatedUtc)
                                                         VALUES (@device,@key,@value,@updated)
                                                         ON CONFLICT DO NOTHING
                                                         """, connection, transaction);
            command.Parameters.AddWithValue("device", mapping.DeviceId);
            command.Parameters.AddWithValue("key", mapping.MappingKey);
            command.Parameters.AddWithValue("value", mapping.MappingValue);
            command.Parameters.AddWithValue("updated", mapping.UpdatedUtc);
            insertedMappings += await command.ExecuteNonQueryAsync();
        }

        if (insertedDevices + insertedLinks + insertedMappings > 0)
        {
            await using var revision = new NpgsqlCommand("""
                                                         UPDATE CloudStateMetadata
                                                         SET Revision = Revision + 1, UpdatedUtc = NOW()
                                                         WHERE StateKey = 'cloud-state'
                                                         """, connection, transaction);
            var revisionRows = await revision.ExecuteNonQueryAsync();
            if (!RecoveryPlanner.IsRevisionUpdateSuccessful(revisionRows))
                throw new InvalidOperationException(
                    "CloudStateMetadata revision update did not affect exactly one row; recovery was rolled back.");
            revisionBumped = true;
        }
        await transaction.CommitAsync();
    }
    else
        await transaction.RollbackAsync();

    return new RecoveryReport(plan.SourceDeviceCount,
        plan.EligibleDevices.Count, plan.ExcludedSyntheticDevices, plan.AlreadyPresentDevices,
        plan.DevicesToInsert.Count, plan.SourceAccountDeviceLinks.Count, plan.AccountDeviceLinksToInsert.Count,
        plan.LinksMissingTargetAccounts, plan.SourceDeviceHostMappings.Count,
        plan.DeviceHostMappingsToInsert.Count, apply, insertedDevices, insertedLinks, insertedMappings,
        revisionBumped);
}

static async Task<HashSet<string>> ReadExistingDeviceIdsAsync(NpgsqlConnection connection,
    NpgsqlTransaction transaction, IReadOnlyCollection<string> ids)
{
    if (ids.Count == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using var command = new NpgsqlCommand(
        "SELECT DeviceId FROM Devices WHERE LOWER(DeviceId) = ANY(@ids)", connection, transaction);
    command.Parameters.AddWithValue("ids", ids.Select(id => id.ToLowerInvariant()).ToArray());
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) result.Add(reader.GetString(0));
    return result;
}

static async Task<HashSet<string>> ReadExistingAccountIdsAsync(NpgsqlConnection connection,
    NpgsqlTransaction transaction, IEnumerable<string> ids)
{
    var values = ids.Distinct(StringComparer.Ordinal).ToArray();
    if (values.Length == 0) return new HashSet<string>(StringComparer.Ordinal);
    await using var command = new NpgsqlCommand(
        "SELECT AccountId FROM Accounts WHERE AccountId = ANY(@ids)", connection, transaction);
    command.Parameters.AddWithValue("ids", values);
    var result = new HashSet<string>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) result.Add(reader.GetString(0));
    return result;
}

static async Task<HashSet<(string AccountId, string DeviceId)>> ReadExistingLinksAsync(
    NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyCollection<RecoveryAccountDeviceLink> links)
{
    var result = new HashSet<(string, string)>();
    foreach (var link in links)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM AccountDevices WHERE AccountId=@account AND DeviceId=@device", connection, transaction);
        command.Parameters.AddWithValue("account", link.AccountId);
        command.Parameters.AddWithValue("device", link.DeviceId);
        if (await command.ExecuteScalarAsync() is not null) result.Add((link.AccountId, link.DeviceId));
    }
    return result;
}

static async Task<HashSet<(string DeviceId, string MappingKey)>> ReadExistingMappingsAsync(
    NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyCollection<RecoveryDeviceMapping> mappings)
{
    var result = new HashSet<(string, string)>();
    foreach (var mapping in mappings)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM DeviceHostMappings WHERE DeviceId=@device AND MappingKey=@key", connection, transaction);
        command.Parameters.AddWithValue("device", mapping.DeviceId);
        command.Parameters.AddWithValue("key", mapping.MappingKey);
        if (await command.ExecuteScalarAsync() is not null) result.Add((mapping.DeviceId, mapping.MappingKey));
    }
    return result;
}

static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
    reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

static DateTimeOffset? NullableDate(NpgsqlDataReader reader, int ordinal) =>
    reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

static void AddRecoveryDeviceParameters(NpgsqlCommand command, RecoveryDevice device)
{
    command.Parameters.AddWithValue("deviceId", device.DeviceId);
    command.Parameters.AddWithValue("robotId", device.RobotId);
    command.Parameters.AddWithValue("friendly", device.FriendlyName);
    command.Parameters.AddWithValue("firmware", (object?)device.FirmwareVersion ?? DBNull.Value);
    command.Parameters.AddWithValue("application", (object?)device.ApplicationVersion ?? DBNull.Value);
    command.Parameters.AddWithValue("active", device.IsActive);
    command.Parameters.AddWithValue("certificate", (object?)device.CertificateThumbprint ?? DBNull.Value);
    command.Parameters.AddWithValue("identity", (object?)device.IssuedIdentityId ?? DBNull.Value);
    command.Parameters.AddWithValue("build", (object?)device.BuildHash ?? DBNull.Value);
    command.Parameters.AddWithValue("config", (object?)device.ConfigHash ?? DBNull.Value);
    command.Parameters.AddWithValue("serial", (object?)device.VerifiedSerialNumber ?? DBNull.Value);
    command.Parameters.AddWithValue("serialSource", (object?)device.SerialEvidenceSource ?? DBNull.Value);
    command.Parameters.AddWithValue("serialUtc", (object?)device.SerialEvidenceVerifiedUtc ?? DBNull.Value);
    command.Parameters.AddWithValue("registration", device.RegistrationSource);
    command.Parameters.AddWithValue("hidden", device.IsHidden);
    command.Parameters.AddWithValue("archived", (object?)device.ArchivedUtc ?? DBNull.Value);
    command.Parameters.AddWithValue("created", device.CreatedUtc);
    command.Parameters.AddWithValue("updated", device.UpdatedUtc);
}
static bool AppliesToTarget(string scriptPath, MigrationTarget target)
{
    var fileName = Path.GetFileName(scriptPath);
    if (fileName.EndsWith(".state.sql", StringComparison.OrdinalIgnoreCase))
        return target == MigrationTarget.State;

    if (fileName.EndsWith(".personal-memory.sql", StringComparison.OrdinalIgnoreCase))
        return target == MigrationTarget.PersonalMemory;

    return true;
}

internal enum MigrationTarget
{
    State,
    PersonalMemory,
    All
}

internal sealed record MigrationOptions(
    MigrationTarget Target,
    bool PreviewOnly,
    bool Verbose,
    string? ScriptsDirectory,
    string? StateConnectionString,
    string? PersonalMemoryConnectionString,
    string? MediaConnectionString,
    string MediaContainerName,
    bool ImportLegacyCloudState,
    bool ImportLegacyPersonalMemory,
    bool Verify,
    bool AuditCloudState,
    bool RecoverMissingDevices,
    bool ApplyRequested,
    bool RecoveryConfirmation,
    string? SourceStateConnectionString,
    string? TargetStateConnectionString,
    bool ShowHelp)
{
    public static string HelpText =>
        """
        Open Jibo migration runner

        Usage:
          dotnet Jibo.Cloud.Migrations.dll --apply
          dotnet Jibo.Cloud.Migrations.dll --preview
          dotnet Jibo.Cloud.Migrations.dll --target state|personal-memory|all
          dotnet Jibo.Cloud.Migrations.dll --audit-cloud-state

        Options:
          --apply                 Apply pending SQL migrations
          --preview, --dry-run    List pending migrations without applying them
          --target                Choose a target database
          --scripts               Override the SQL script directory
          --state-connection      Override the state database connection string
          --memory-connection     Override the personal memory connection string
          --media-connection      Azure Blob connection string for imported backup payloads
          --media-container       Azure Blob media container (default: openjibo-media)
          --import-legacy-cloud-state
                                  Explicitly import PersistenceSnapshots/cloud-state
          --import-legacy-personal-memory
                                  Explicitly import PersistenceSnapshots/personal-memory
          --verify                Fail unless legacy snapshots have matching import ledgers
          --audit-cloud-state     Read-only aggregate comparison of legacy and normalized cloud state
          --recover-missing-devices
                                  Compare/recover missing non-synthetic Devices, links, and mappings
          --source-state-connection
                                  Preserved normalized PostgreSQL source (read-only)
          --target-state-connection
                                  Current normalized PostgreSQL target
          --confirm-recover-missing-devices
                                  Required with --apply for recovery mutations
          --verbose               Print already-applied scripts too
          --help                  Show this help
        """;

    public static MigrationOptions Parse(string[] args)
    {
        var target = MigrationTarget.All;
        var previewOnly = false;
        var verbose = false;
        string? scriptsDirectory = null;
        var stateConnectionString = Environment.GetEnvironmentVariable("OpenJibo__State__ConnectionString")
                                    ?? Environment.GetEnvironmentVariable("OPENJIBO_STATE_STORAGE_CONNECTION_STRING")
                                    ?? BuildPostgreSqlConnectionString("openjibo_state");
        var personalMemoryConnectionString = Environment.GetEnvironmentVariable(
                                                 "OpenJibo__PersonalMemory__ConnectionString")
                                             ?? Environment.GetEnvironmentVariable(
                                                 "OPENJIBO_PERSONAL_MEMORY_STORAGE_CONNECTION_STRING")
                                             ?? BuildPostgreSqlConnectionString("openjibo_memory");
        var mediaConnectionString = Environment.GetEnvironmentVariable("OpenJibo__Media__ConnectionString")
                                    ?? Environment.GetEnvironmentVariable(
                                        "OPENJIBO_MEDIA_STORAGE_CONNECTION_STRING");
        var mediaContainerName = Environment.GetEnvironmentVariable("OpenJibo__Media__ContainerName")
                                 ?? "openjibo-media";
        var showHelp = false;
        var importLegacyCloudState = false;
        var importLegacyPersonalMemory = false;
        var verify = false;
        var auditCloudState = false;
        var recoverMissingDevices = false;
        var applyRequested = false;
        var recoveryConfirmation = false;
        string? sourceStateConnectionString = Environment.GetEnvironmentVariable("OPENJIBO_SOURCE_STATE_CONNECTION_STRING");
        string? targetStateConnectionString = Environment.GetEnvironmentVariable("OPENJIBO_TARGET_STATE_CONNECTION_STRING");

        for (var index = 0; index < args.Length; index += 1)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;
                case "--preview":
                case "--dry-run":
                    previewOnly = true;
                    break;
                case "--apply":
                    previewOnly = false;
                    applyRequested = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--import-legacy-cloud-state":
                    importLegacyCloudState = true;
                    break;
                case "--import-legacy-personal-memory":
                    importLegacyPersonalMemory = true;
                    break;
                case "--verify":
                    verify = true;
                    break;
                case "--audit-cloud-state":
                    auditCloudState = true;
                    break;
                case "--recover-missing-devices":
                    recoverMissingDevices = true;
                    break;
                case "--confirm-recover-missing-devices":
                    recoveryConfirmation = true;
                    break;
                case "--source-state-connection":
                    sourceStateConnectionString = GetValue(args, ref index, "--source-state-connection");
                    break;
                case "--target-state-connection":
                    targetStateConnectionString = GetValue(args, ref index, "--target-state-connection");
                    break;
                case "--target":
                    target = ParseTarget(GetValue(args, ref index, "--target"));
                    break;
                case "--scripts":
                    scriptsDirectory = GetValue(args, ref index, "--scripts");
                    break;
                case "--state-connection":
                    stateConnectionString = GetValue(args, ref index, "--state-connection");
                    break;
                case "--memory-connection":
                    personalMemoryConnectionString = GetValue(args, ref index, "--memory-connection");
                    break;
                case "--media-connection":
                    mediaConnectionString = GetValue(args, ref index, "--media-connection");
                    break;
                case "--media-container":
                    mediaContainerName = GetValue(args, ref index, "--media-container") ?? "openjibo-media";
                    break;
            }
        }

        return new MigrationOptions(
            target,
            previewOnly,
            verbose,
            scriptsDirectory,
            stateConnectionString,
            personalMemoryConnectionString,
            mediaConnectionString,
            mediaContainerName,
            importLegacyCloudState,
            importLegacyPersonalMemory,
            verify,
            auditCloudState,
            recoverMissingDevices,
            applyRequested,
            recoveryConfirmation,
            sourceStateConnectionString,
            targetStateConnectionString,
            showHelp);
    }

    private static string BuildPostgreSqlConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_HOST") ?? "postgres",
            Port = int.TryParse(Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_PORT"), out var port)
                ? port
                : 5432,
            Database = databaseName,
            Username = Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_USER") ?? "openjibo"
        };

        var password = Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_PASSWORD");
        if (!string.IsNullOrWhiteSpace(password))
            builder.Password = password;

        return builder.ConnectionString;
    }

    public string? ResolveConnectionString(MigrationTarget target)
    {
        return target switch
        {
            MigrationTarget.State => StateConnectionString,
            MigrationTarget.PersonalMemory => PersonalMemoryConnectionString,
            _ => null
        };
    }

    private static MigrationTarget ParseTarget(string? value)
    {
        return string.Equals(value, "state", StringComparison.OrdinalIgnoreCase)
            ? MigrationTarget.State
            : string.Equals(value, "personal-memory", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(value, "memory", StringComparison.OrdinalIgnoreCase)
                ? MigrationTarget.PersonalMemory
                : MigrationTarget.All;
    }

    private static string GetValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {optionName}.");

        index += 1;
        return args[index];
    }
}
