using System.Security.Cryptography;
using System.Text.Json;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed partial class PostgreSqlCloudStateStore
{
    private static readonly JsonSerializerOptions BackupJsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<UpdateManifest> ListUpdates(string? subsystem = null, string? filter = null) =>
        Sync(RequireUpdates().ListAsync(NormalizeUpdateSubsystem(subsystem), filter))
            .Select(item => item.Manifest).ToArray();

    public UpdateManifest? GetUpdateFrom(string? subsystem, string? fromVersion, string? filter) =>
        ListUpdates(subsystem, filter).FirstOrDefault(update => IsUpdateNewerThanRequest(update.ToVersion, fromVersion));

    public UpdateManifest CreateUpdate(string? fromVersion, string? toVersion, string? changes, string? shaHash,
        long? length, string? subsystem, string? filter, IDictionary<string, object?>? dependencies)
    {
        var updateId = $"upd-{Guid.NewGuid():N}";
        var manifest = new UpdateManifest
        {
            UpdateId = updateId,
            FromVersion = fromVersion ?? "unknown",
            ToVersion = toVersion ?? fromVersion ?? "unknown",
            Changes = changes ?? string.Empty,
            Url = $"https://api.jibo.com/update/{updateId}",
            ShaHash = shaHash ?? "fake-sha-hash",
            Length = Math.Max(0, length ?? 0),
            Subsystem = subsystem ?? "unknown",
            Filter = filter
        };
        return Sync(RequireUpdates().UpsertAsync(new StoredUpdateManifest(manifest,
            dependencies is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(dependencies)))).Manifest;
    }

    public UpdateManifest RemoveUpdate(string? updateId) =>
        string.IsNullOrWhiteSpace(updateId)
            ? MissingUpdate(updateId)
            : Sync(RequireUpdates().DeleteAsync(updateId))?.Manifest ?? MissingUpdate(updateId);

    public IReadOnlyList<MediaRecord> ListMedia(IReadOnlyList<string>? loopIds = null, long? after = null,
        long? before = null) => Sync(RequireMedia().ListAsync(GetAccount().AccountId, loopIds,
        FromUnixMilliseconds(after), FromUnixMilliseconds(before), 1000)).Select(item => item.Media).ToArray();

    public IReadOnlyList<MediaRecord> GetMedia(IReadOnlyList<string> paths) =>
        Sync(RequireMedia().GetAsync(GetAccount().AccountId, paths)).Select(item => item.Media).ToArray();

    public IReadOnlyList<MediaRecord> RemoveMedia(IReadOnlyList<string> paths) =>
        Sync(RequireMedia().SoftDeleteAsync(GetAccount().AccountId, paths)).Select(item => item.Media).ToArray();

    public MediaRecord CreateMedia(string loopId, string path, string type, string reference, bool isEncrypted,
        IDictionary<string, object?>? meta)
    {
        var media = new MediaRecord
        {
            Path = path,
            MediaType = type,
            Reference = reference,
            AccountId = GetAccount().AccountId,
            LoopId = loopId,
            Url = $"https://api.jibo.com/media/{Uri.EscapeDataString(path)}",
            IsEncrypted = isEncrypted,
            Meta = meta ?? new Dictionary<string, object?>()
        };
        return Sync(RequireMedia().UpsertAsync(new StoredMediaRecord(media))).Media;
    }

    public IReadOnlyList<BackupRecord> GetBackups() =>
        Sync(RequireBackups().ListAsync(GetAccount().AccountId)).Select(ToBackupRecord).ToArray();

    public BackupRecord CreateBackup(string loopId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loopId);
        var accountId = GetAccount().AccountId;
        var topology = GetLoops().FirstOrDefault(loop =>
            loop.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase));
        if (topology is null) throw new InvalidOperationException($"Loop '{loopId}' was not found.");
        var resolvedLoopId = topology.LoopId;
        var deviceLinks = Sync(RequireBackupLoops().GetAsync(accountId, resolvedLoopId))?.Devices ??
                          ResolveBackupDeviceLinks(topology);

        var payload = new RelationalLoopBackup(
            topology,
            deviceLinks.ToArray(),
            GetLoopMembers(resolvedLoopId).ToArray(),
            GetPeople(resolvedLoopId).ToArray(),
            GetHolidays(resolvedLoopId).ToArray(),
            GetCommuteProfiles(resolvedLoopId).ToArray(),
            GetCalendarEvents(resolvedLoopId).ToArray(),
            GetGreetingPresences(resolvedLoopId).ToArray(),
            _recognition is null ? [] : Sync(_recognition.ListAllForBackupAsync(accountId, resolvedLoopId)).ToArray(),
            _loopKeys is null ? null : Sync(_loopKeys.GetAsync(accountId, resolvedLoopId)),
            _loopKeys is null ? [] : Sync(_loopKeys.ListAllRequestsForBackupAsync(accountId, resolvedLoopId)).ToArray(),
            _media is null ? [] : Sync(_media.ListAllForBackupAsync(accountId, resolvedLoopId)).ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, BackupJsonOptions);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var backupId = Guid.NewGuid().ToString("N");
        var uri = Sync(RequireBackupPayloads().StoreAsync(
            $"cloud-backups/{accountId}/{backupId}-{sha256}.json", bytes, sha256));
        var storedBytes = Sync(RequireBackupPayloads().LoadAsync(uri));
        if (storedBytes is null || storedBytes.LongLength != bytes.LongLength ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(storedBytes), SHA256.HashData(bytes)))
            throw new InvalidDataException("The external backup payload failed write verification.");
        var manifest = new BackupManifestRecord(backupId, accountId, resolvedLoopId,
            string.IsNullOrWhiteSpace(name) ? "backup" : name.Trim(), uri, sha256, bytes.LongLength, 2,
            "available", DateTimeOffset.UtcNow);
        return ToBackupRecord(Sync(RequireBackups().UpsertAsync(manifest)));
    }

    public BackupRecord? RestoreBackup(string? backupId = null)
    {
        var accountId = GetAccount().AccountId;
        var manifest = string.IsNullOrWhiteSpace(backupId)
            ? Sync(RequireBackups().ListAsync(accountId)).FirstOrDefault()
            : Sync(RequireBackups().GetAsync(accountId, backupId));
        if (manifest is null) return null;

        var bytes = Sync(RequireBackupPayloads().LoadAsync(manifest.BlobUri));
        if (bytes is null || bytes.LongLength != manifest.ContentLength ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(manifest.ContentSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The backup payload is missing or failed integrity verification.");
        RelationalLoopBackup payload;
        try
        {
            payload = manifest.BackupSchemaVersion switch
            {
                1 => AdaptLegacyBackup(JsonSerializer.Deserialize<LegacyCloudStateSnapshot>(bytes,
                         BackupJsonOptions) ?? throw new InvalidDataException("The legacy backup payload is invalid."),
                    manifest, accountId),
                2 => JsonSerializer.Deserialize<RelationalLoopBackup>(bytes, BackupJsonOptions) ??
                     throw new InvalidDataException("The backup payload is invalid."),
                _ => throw new InvalidDataException(
                    $"Backup schema version {manifest.BackupSchemaVersion} is not supported.")
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The backup payload is invalid.", exception);
        }
        ValidateBackupScope(payload, manifest, accountId);

        var restoredUtc = DateTimeOffset.UtcNow;
        if (_atomicBackupRestorer is not null)
            Sync(_atomicBackupRestorer.RestoreAsync(accountId, manifest, payload,
                restoredUtc));
        else
        {
            Sync(RequireBackupLoops().UpsertAsync(new StoredLoopTopology(payload.Loop, payload.Devices)));
            foreach (var member in payload.Members) Sync(RequireBackupMembers().UpsertAsync(accountId, member));
            foreach (var person in payload.People) Sync(RequireBackupPeople().UpsertAsync(person));
            foreach (var holiday in payload.Holidays) UpsertHoliday(holiday);
            foreach (var commute in payload.Commutes) UpsertCommuteProfile(commute);
            foreach (var calendarEvent in payload.CalendarEvents) UpsertCalendarEvent(calendarEvent);
            foreach (var greeting in payload.Greetings) UpsertGreetingPresence(greeting);
            if (_recognition is not null)
                foreach (var observation in payload.RecognitionObservations)
                    Sync(_recognition.AddAsync(accountId, observation));
            if (_loopKeys is not null)
            {
                if (payload.LoopKey is not null) Sync(_loopKeys.UpsertAsync(accountId, payload.LoopKey));
                foreach (var request in payload.KeyRequests) Sync(_loopKeys.UpsertRequestAsync(accountId, request));
            }
            if (_media is not null)
                foreach (var media in payload.Media) Sync(_media.UpsertAsync(media));
            _ = Sync(RequireBackups().MarkRestoredAsync(accountId, manifest.BackupId, restoredUtc));
        }
        return ToBackupRecord(manifest);
    }

    private IReadOnlyList<LoopDeviceLink> ResolveBackupDeviceLinks(LoopRecord loop)
    {
        var device = !string.IsNullOrWhiteSpace(loop.RobotFriendlyId)
            ? FindDeviceByFriendlyId(loop.RobotFriendlyId)
            : null;
        device ??= GetDevices().FirstOrDefault(candidate =>
            string.Equals(candidate.RobotId, loop.RobotId, StringComparison.OrdinalIgnoreCase));
        return device is null ? [] : [new LoopDeviceLink(device.DeviceId, true, DateTimeOffset.UtcNow)];
    }

    private static BackupRecord ToBackupRecord(BackupManifestRecord manifest) => new()
    {
        BackupId = manifest.BackupId,
        CreatedUtc = manifest.CreatedUtc,
        LoopId = manifest.LoopId,
        Name = manifest.Name,
        SnapshotJson = null
    };

    private static UpdateManifest MissingUpdate(string? updateId) => new()
    {
        UpdateId = updateId ?? "unknown-update",
        Changes = "Update not found",
        Url = "https://api.jibo.com/update/missing",
        ShaHash = "missing",
        Subsystem = "unknown"
    };

    private static DateTimeOffset? FromUnixMilliseconds(long? value) =>
        value is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(value.Value);

    private static string? NormalizeUpdateSubsystem(string? subsystem)
    {
        if (string.IsNullOrWhiteSpace(subsystem)) return null;
        var normalized = subsystem.Trim();
        return normalized.Equals("all", StringComparison.OrdinalIgnoreCase) || normalized == "*"
            ? null
            : normalized;
    }

    private static void ValidateBackupScope(RelationalLoopBackup payload, BackupManifestRecord manifest,
        string accountId)
    {
        if (manifest.BackupSchemaVersion is not (1 or 2) || payload.Loop is null ||
            !string.Equals(payload.Loop.LoopId, manifest.LoopId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(payload.Loop.OwnerAccountId, accountId, StringComparison.OrdinalIgnoreCase) ||
            payload.Devices is null || payload.Members is null || payload.People is null || payload.Holidays is null ||
            payload.Commutes is null || payload.CalendarEvents is null || payload.Greetings is null ||
            payload.RecognitionObservations is null || payload.KeyRequests is null || payload.Media is null ||
            payload.Devices.Any(item => string.IsNullOrWhiteSpace(item.DeviceId)) ||
            payload.Devices.Count(item => item.IsPrimary) > 1 ||
            payload.Devices.Select(item => item.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            payload.Devices.Length ||
            payload.Members.Any(item => !SameLoop(item.LoopId, manifest.LoopId)) ||
            payload.People.Any(item => !SameLoop(item.LoopId, manifest.LoopId) ||
                                       !string.Equals(item.AccountId, accountId, StringComparison.OrdinalIgnoreCase)) ||
            payload.Holidays.Any(item => !SameLoop(item.LoopId, manifest.LoopId)) ||
            payload.Commutes.Any(item => !SameLoop(item.LoopId, manifest.LoopId)) ||
            payload.CalendarEvents.Any(item => !SameLoop(item.LoopId, manifest.LoopId)) ||
            payload.Greetings.Any(item => !SameLoop(item.LoopId, manifest.LoopId) ||
                                          !string.Equals(item.AccountId, accountId,
                                              StringComparison.OrdinalIgnoreCase)) ||
            payload.RecognitionObservations.Any(item => !SameLoop(item.LoopId, manifest.LoopId)) ||
            payload.LoopKey is not null && !SameLoop(payload.LoopKey.LoopId, manifest.LoopId) ||
            payload.KeyRequests.Any(item => !SameLoop(item.Request.LoopId, manifest.LoopId)) ||
            payload.Media.Any(item => !SameLoop(item.Media.LoopId, manifest.LoopId) ||
                                      !string.Equals(item.Media.AccountId, accountId,
                                          StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The backup payload does not match its account and loop manifest.");
    }

    private static bool SameLoop(string? candidate, string? expected) =>
        !string.IsNullOrWhiteSpace(expected) &&
        string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase);

    private RelationalLoopBackup AdaptLegacyBackup(LegacyCloudStateSnapshot source,
        BackupManifestRecord manifest, string accountId)
    {
        var loopId = manifest.LoopId ?? throw new InvalidDataException(
            "A legacy backup without a loop scope cannot be restored into normalized storage.");
        var loop = (source.Loops ?? []).FirstOrDefault(item => SameLoop(item.LoopId, loopId)) ??
                   throw new InvalidDataException("The legacy backup does not contain its manifest loop.");
        var devices = source.AllDevices().Where(device =>
                string.Equals(device.RobotId, loop.RobotId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(device.FriendlyName, loop.RobotFriendlyId, StringComparison.OrdinalIgnoreCase))
            .Select((device, index) => new LoopDeviceLink(device.DeviceId, index == 0, loop.CreatedUtc))
            .ToArray();
        var legacyKey = (source.SymmetricKeys ?? []).FirstOrDefault(item => SameLoop(item.Key, loopId));
        LoopSymmetricKeyRecord? key = null;
        if (!string.IsNullOrWhiteSpace(legacyKey.Key))
        {
            var protector = _secretProtector ?? throw new InvalidOperationException(
                "A cloud-state secret protector is required to restore a legacy loop key.");
            key = new LoopSymmetricKeyRecord(loopId, protector.Protect(legacyKey.Value), protector.KeyId,
                "AES-256-GCM", DateTimeOffset.UtcNow);
        }

        return new RelationalLoopBackup(loop, devices,
            (source.LoopMembers ?? []).Where(item => SameLoop(item.LoopId, loopId)).ToArray(),
            (source.People ?? []).Where(item => SameLoop(item.LoopId, loopId)).ToArray(),
            (source.Holidays ?? []).Where(item => SameLoop(item.LoopId, loopId)).ToArray(),
            (source.CommuteProfiles ?? []).Where(item => SameLoop(item.LoopId, loopId)).ToArray(),
            (source.CalendarEvents ?? []).Where(item => SameLoop(item.LoopId, loopId)).ToArray(),
            (source.GreetingPresences ?? []).Where(item => SameLoop(item.LoopId, loopId)).ToArray(),
            (source.RecognitionObservations ?? []).Where(item => SameLoop(item.LoopId, loopId)).ToArray(),
            key,
            (source.KeyRequests ?? []).Where(item => SameLoop(item.LoopId, loopId))
            .Select(item => new StoredKeyRequest(item)).ToArray(),
            (source.Media ?? []).Where(item => SameLoop(item.LoopId, loopId) &&
                                               string.Equals(item.AccountId, accountId,
                                                   StringComparison.OrdinalIgnoreCase))
            .Select(item => new StoredMediaRecord(item)).ToArray());
    }

    private static bool IsUpdateNewerThanRequest(string candidate, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return true;
        if (Version.TryParse(candidate, out var candidateVersion) && Version.TryParse(requested, out var requestedVersion))
            return candidateVersion > requestedVersion;
        return string.Compare(candidate, requested, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private IUpdateManifestRepository RequireUpdates() => _updates ?? MissingRepository<IUpdateManifestRepository>();
    private IMediaMetadataRepository RequireMedia() => _media ?? MissingRepository<IMediaMetadataRepository>();
    private IBackupManifestRepository RequireBackups() => _backups ?? MissingRepository<IBackupManifestRepository>();
    private ILoopTopologyRepository RequireBackupLoops() => _loops ?? MissingRepository<ILoopTopologyRepository>();
    private ILoopMemberRepository RequireBackupMembers() => _members ?? MissingRepository<ILoopMemberRepository>();
    private IPersonRepository RequireBackupPeople() => _people ?? MissingRepository<IPersonRepository>();
    private IBackupPayloadStore RequireBackupPayloads() => _backupPayloads ??
        throw new InvalidOperationException("A backup payload store is required for normalized cloud-state backups.");

    private static T MissingRepository<T>() =>
        throw new InvalidOperationException($"The normalized {typeof(T).Name} is not configured.");

}

internal sealed record RelationalLoopBackup(LoopRecord Loop, LoopDeviceLink[] Devices,
    LoopMemberRecord[] Members, PersonRecord[] People,
    HolidayRecord[] Holidays, CommuteProfileRecord[] Commutes, CalendarEventRecord[] CalendarEvents,
    GreetingPresenceRecord[] Greetings, RecognitionObservationRecord[] RecognitionObservations,
    LoopSymmetricKeyRecord? LoopKey, StoredKeyRequest[] KeyRequests, StoredMediaRecord[] Media);
