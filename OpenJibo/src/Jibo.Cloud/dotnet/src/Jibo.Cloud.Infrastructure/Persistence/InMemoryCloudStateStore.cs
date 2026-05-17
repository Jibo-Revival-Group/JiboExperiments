using System.Collections.Concurrent;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class InMemoryCloudStateStore : ICloudStateStore
{
    private static readonly JsonSerializerOptions PersistenceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private AccountProfile _account = new();
    private readonly ConcurrentDictionary<string, DeviceRegistration> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CloudSession> _sessionsByToken = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _symmetricKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, KeyRequestRecord> _keyRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISnapshotStore _snapshotStore;
    private readonly Lock _syncRoot = new();
    private readonly List<UpdateManifest> _updates;
    private readonly List<MediaRecord> _media = [];
    private readonly List<BackupRecord> _backups = [];
    private readonly List<LoopRecord> _loops;
    private readonly List<PersonRecord> _people;
    private DeviceRegistration _robot;
    private RobotProfile _robotProfile;
    private long _revision;
    private DateTimeOffset? _lastLoadedUtc;
    private DateTimeOffset? _lastSavedUtc;

    public InMemoryCloudStateStore(string? persistencePath = null)
        : this(new JsonFileSnapshotStore(persistencePath, PersistenceJsonOptions))
    {
    }

    public InMemoryCloudStateStore(ISnapshotStore snapshotStore)
    {
        _snapshotStore = snapshotStore;
        _robot = new DeviceRegistration
        {
            HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["api.jibo.com"] = "openjibo.com",
                ["api-socket.jibo.com"] = "openjibo.com",
                ["neo-hub.jibo.com"] = "openjibo.com"
            }
        };

        _devices[_robot.DeviceId] = _robot;
        _robotProfile = new RobotProfile
        {
            RobotId = _robot.RobotId,
            Payload = new Dictionary<string, object?>
            {
                ["SSID"] = "my-ssid",
                ["connectedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["platform"] = "12.10.0",
                ["serialNumber"] = _robot.DeviceId
            }
        };
        _loops =
        [
            new LoopRecord
            {
                OwnerAccountId = _account.AccountId,
                RobotId = _robot.RobotId,
                RobotFriendlyId = _robot.DeviceId
            }
        ];
        _people =
        [
            new PersonRecord
            {
                PersonId = "person-openjibo-owner",
                AccountId = _account.AccountId,
                LoopId = _loops[0].LoopId,
                RobotId = _robot.RobotId,
                DisplayName = $"{_account.FirstName} {_account.LastName}",
                Alias = _account.FirstName,
                IsPrimary = true
            },
            new PersonRecord
            {
                PersonId = "person-openjibo-household-member",
                AccountId = _account.AccountId,
                LoopId = _loops[0].LoopId,
                RobotId = _robot.RobotId,
                DisplayName = "OpenJibo Household Member",
                Alias = "Household Member",
                IsPrimary = false
            }
        ];

        _updates = [];
        LoadPersistedState();
    }

    public PersistenceStateInfo GetPersistenceStateInfo()
    {
        return new PersistenceStateInfo(
            SchemaVersion: CurrentSchemaVersion,
            Revision: Interlocked.Read(ref _revision),
            LastLoadedUtc: _lastLoadedUtc,
            LastSavedUtc: _lastSavedUtc);
    }

    public void LoadPersistedState()
    {
        var snapshot = _snapshotStore.Load<PersistentStateSnapshot>();
        if (snapshot is null)
        {
            return;
        }

        _account = snapshot.Account ?? _account;
        _robot = snapshot.Robot ?? _robot;
        _robotProfile = snapshot.RobotProfile ?? _robotProfile;

        _devices.Clear();
        foreach (var device in snapshot.Devices ?? [])
        {
            _devices[device.DeviceId] = device;
        }

        if (_devices.IsEmpty || !_devices.ContainsKey(_robot.DeviceId))
        {
            _devices[_robot.DeviceId] = _robot;
        }

        _sessionsByToken.Clear();
        foreach (var session in snapshot.Sessions ?? [])
        {
            if (!string.IsNullOrWhiteSpace(session.Token))
            {
                _sessionsByToken[session.Token] = session.ToRecord();
            }
        }

        _symmetricKeys.Clear();
        foreach (var pair in snapshot.SymmetricKeys ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            _symmetricKeys[pair.Key] = pair.Value;
        }

        _keyRequests.Clear();
        foreach (var keyRequest in snapshot.KeyRequests ?? [])
        {
            _keyRequests[keyRequest.RequestId] = keyRequest;
        }

        _updates.Clear();
        _updates.AddRange(snapshot.Updates ?? []);

        _media.Clear();
        _media.AddRange(snapshot.Media ?? []);

        _backups.Clear();
        _backups.AddRange(snapshot.Backups ?? []);

        _loops.Clear();
        _loops.AddRange(snapshot.Loops ?? []);

        _people.Clear();
        _people.AddRange(snapshot.People ?? []);

        if (_robotProfile is null || !string.Equals(_robotProfile.RobotId, _robot.RobotId, StringComparison.OrdinalIgnoreCase))
        {
            _robotProfile = new RobotProfile
            {
                RobotId = _robot.RobotId,
                Payload = new Dictionary<string, object?>
                {
                    ["SSID"] = "my-ssid",
                    ["connectedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ["platform"] = _robot.FirmwareVersion ?? "12.10.0",
                    ["serialNumber"] = _robot.DeviceId
                },
                UpdatedUtc = DateTimeOffset.UtcNow
            };
        }

        Interlocked.Exchange(ref _revision, snapshot.Revision);
        _lastLoadedUtc = snapshot.LastLoadedUtc ?? DateTimeOffset.UtcNow;
        _lastSavedUtc = snapshot.LastSavedUtc;
    }

    public void SavePersistedState()
    {
        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var snapshot = new PersistentStateSnapshot
            {
                SchemaVersion = CurrentSchemaVersion,
                Revision = Interlocked.Read(ref _revision),
                LastLoadedUtc = _lastLoadedUtc,
                LastSavedUtc = now,
                Account = _account,
                Robot = _robot,
                RobotProfile = _robotProfile,
                Devices = _devices.Values.ToArray(),
                Sessions = _sessionsByToken.Values.Select(MapSessionSnapshot).ToArray(),
                SymmetricKeys = _symmetricKeys.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
                KeyRequests = _keyRequests.Values.ToArray(),
                Updates = _updates.ToArray(),
                Media = _media.ToArray(),
                Backups = _backups.ToArray(),
                Loops = _loops.ToArray(),
                People = _people.ToArray()
            };
            _snapshotStore.Save(snapshot);
            _lastSavedUtc = now;
        }
    }

    public AccountProfile GetAccount() => _account;

    public DeviceRegistration GetRobot() => _robot;

    public RobotProfile GetRobotProfile() => _robotProfile;

    public DeviceRegistration GetOrCreateDevice(string deviceId, string? firmwareVersion, string? applicationVersion)
    {
        var device = _devices.AddOrUpdate(
            deviceId,
            _ => new DeviceRegistration
            {
                DeviceId = deviceId,
                RobotId = $"robot-{deviceId}",
                FriendlyName = "OpenJibo Registered Robot",
                FirmwareVersion = firmwareVersion,
                ApplicationVersion = applicationVersion
            },
            (_, current) => new DeviceRegistration
            {
                DeviceId = current.DeviceId,
                RobotId = current.RobotId,
                FriendlyName = current.FriendlyName,
                FirmwareVersion = firmwareVersion ?? current.FirmwareVersion,
                ApplicationVersion = applicationVersion ?? current.ApplicationVersion,
                HostMappings = new Dictionary<string, string>(current.HostMappings, StringComparer.OrdinalIgnoreCase)
            });

        TouchState();
        return device;
    }

    public string IssueHubToken()
    {
        var token = $"hub-{_account.AccountId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _sessionsByToken[token] = new CloudSession
        {
            Kind = "hub",
            AccountId = _account.AccountId,
            Token = token,
            DeviceId = _robot.DeviceId,
            Metadata = BuildSessionMetadata(_account.AccountId, _robot.DeviceId, ResolveDefaultLoopId())
        };

        TouchState();
        return token;
    }

    public string IssueRobotToken(string deviceId)
    {
        var token = $"token-{deviceId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _sessionsByToken[token] = new CloudSession
        {
            Kind = "robot",
            AccountId = _account.AccountId,
            Token = token,
            DeviceId = deviceId,
            Metadata = BuildSessionMetadata(_account.AccountId, deviceId, ResolveDefaultLoopId())
        };

        TouchState();
        return token;
    }

    public CloudSession OpenSession(string kind, string? deviceId, string? token, string? hostName, string? path)
    {
        var resolvedDeviceId = deviceId ?? _robot.DeviceId;
        var resolvedLoopId = ResolveDefaultLoopId();
        var session = new CloudSession
        {
            Kind = kind,
            AccountId = _account.AccountId,
            DeviceId = resolvedDeviceId,
            Token = token,
            HostName = hostName,
            Path = path,
            Metadata = BuildSessionMetadata(_account.AccountId, resolvedDeviceId, resolvedLoopId)
        };

        if (!string.IsNullOrWhiteSpace(token))
        {
            _sessionsByToken[token] = session;
            TouchState();
        }

        return session;
    }

    public CloudSession? FindSessionByToken(string token)
    {
        return _sessionsByToken.GetValueOrDefault(token);
    }

    public IReadOnlyList<LoopRecord> GetLoops() => _loops.ToArray();

    public IReadOnlyList<PersonRecord> GetPeople() => _people.ToArray();

    public IReadOnlyList<UpdateManifest> ListUpdates(string? subsystem = null, string? filter = null)
    {
        return _updates
            .Where(update => subsystem is null || update.Subsystem.Equals(subsystem, StringComparison.OrdinalIgnoreCase))
            .Where(update => filter is null || string.Equals(update.Filter, filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public UpdateManifest? GetUpdateFrom(string? subsystem, string? fromVersion, string? filter)
    {
        return ListUpdates(subsystem, filter)
            .FirstOrDefault(update => fromVersion is null || update.FromVersion.Equals(fromVersion, StringComparison.OrdinalIgnoreCase));
    }

    public UpdateManifest CreateUpdate(string? fromVersion, string? toVersion, string? changes, string? shaHash, long? length, string? subsystem, string? filter, IDictionary<string, object?>? dependencies)
    {
        var update = new UpdateManifest
        {
            UpdateId = $"upd-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            FromVersion = fromVersion ?? "unknown",
            ToVersion = toVersion ?? fromVersion ?? "unknown",
            Changes = changes ?? string.Empty,
            Url = $"https://api.jibo.com/update/upd-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            ShaHash = shaHash ?? "fake-sha-hash",
            Length = length ?? 0,
            Subsystem = subsystem ?? "unknown",
            Filter = filter
        };

        _updates.Add(update);
        TouchState();
        return update;
    }

    public UpdateManifest RemoveUpdate(string? updateId)
    {
        var existing = _updates.FirstOrDefault(update => update.UpdateId == updateId);
        if (existing is null)
        {
            return new UpdateManifest
            {
                UpdateId = updateId ?? "unknown-update",
                Changes = "Update not found",
                Url = "https://api.jibo.com/update/missing",
                ShaHash = "missing",
                Subsystem = "unknown"
            };
        }

        _updates.Remove(existing);
        TouchState();
        return existing;
    }

    public IReadOnlyList<MediaRecord> ListMedia(IReadOnlyList<string>? loopIds = null, long? after = null, long? before = null)
    {
        return _media
            .Where(item => !item.IsDeleted)
            .Where(item => loopIds is null || loopIds.Count == 0 || loopIds.Contains(item.LoopId))
            .Where(item => after is null || item.CreatedUtc.ToUnixTimeMilliseconds() > after)
            .Where(item => before is null || item.CreatedUtc.ToUnixTimeMilliseconds() < before)
            .ToArray();
    }

    public IReadOnlyList<MediaRecord> GetMedia(IReadOnlyList<string> paths)
    {
        return _media.Where(item => paths.Contains(item.Path)).ToArray();
    }

    public IReadOnlyList<MediaRecord> RemoveMedia(IReadOnlyList<string> paths)
    {
        var replacements = new List<MediaRecord>();
        for (var i = 0; i < _media.Count; i++)
        {
            if (!paths.Contains(_media[i].Path))
            {
                continue;
            }

            var updated = new MediaRecord
            {
                Path = _media[i].Path,
                CreatedUtc = _media[i].CreatedUtc,
                MediaType = _media[i].MediaType,
                Reference = _media[i].Reference,
                AccountId = _media[i].AccountId,
                LoopId = _media[i].LoopId,
                Url = _media[i].Url,
                IsEncrypted = _media[i].IsEncrypted,
                IsDeleted = true,
                Meta = _media[i].Meta
            };

            _media[i] = updated;
            replacements.Add(updated);
        }

        if (replacements.Count > 0)
        {
            TouchState();
        }

        return replacements;
    }

    public MediaRecord CreateMedia(string loopId, string path, string type, string reference, bool isEncrypted, IDictionary<string, object?>? meta)
    {
        var item = new MediaRecord
        {
            Path = path,
            MediaType = type,
            Reference = reference,
            AccountId = _account.AccountId,
            LoopId = loopId,
            Url = $"https://api.jibo.com/media/{Uri.EscapeDataString(path)}",
            IsEncrypted = isEncrypted,
            Meta = meta ?? new Dictionary<string, object?>()
        };

        var existingIndex = _media.FindIndex(existing => existing.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            _media[existingIndex] = item;
        }
        else
        {
            _media.Add(item);
        }

        TouchState();
        return item;
    }

    public IReadOnlyList<BackupRecord> GetBackups() => _backups.ToArray();

    public bool ShouldCreateSymmetricKey(string loopId) => !_symmetricKeys.ContainsKey(loopId);

    public string GetOrCreateSymmetricKey(string loopId)
    {
        if (_symmetricKeys.TryGetValue(loopId, out var existing))
        {
            return existing;
        }

        var key = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"open-jibo-symmetric-key:{loopId}"));
        if (_symmetricKeys.TryAdd(loopId, key))
        {
            TouchState();
            return key;
        }

        return _symmetricKeys[loopId];
    }

    public KeyRequestRecord CreateKeyRequest(string loopId, string publicKey)
    {
        var record = new KeyRequestRecord
        {
            RequestId = $"req-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            LoopId = loopId,
            PublicKey = publicKey
        };

        _keyRequests[record.RequestId] = record;
        TouchState();
        return record;
    }

    public KeyRequestRecord GetKeyRequest(string loopId, string? requestId, string? publicKey)
    {
        if (!string.IsNullOrWhiteSpace(requestId) && _keyRequests.TryGetValue(requestId, out var record))
        {
            return record;
        }

        return new KeyRequestRecord
        {
            RequestId = requestId ?? "unknown-request",
            LoopId = loopId,
            PublicKey = publicKey ?? string.Empty
        };
    }

    public IReadOnlyList<KeyRequestRecord> GetIncomingKeyRequests() => [];

    public IReadOnlyList<KeyRequestRecord> GetBinaryRequests() => [];

    public IReadOnlyList<object> GetHolidays()
    {
        return
        [
            new
            {
                id = "easter-1",
                eventId = (string?)null,
                name = "Easter",
                category = "holiday",
                subcategory = (string?)null,
                loopId = _loops[0].LoopId,
                memberId = (string?)null,
                isEnabled = true,
                date = "2026-04-05",
                endDate = (string?)null,
                created = DateTimeOffset.UtcNow.ToString("O")
            }
        ];
    }

    public void UpdateRobot(DeviceRegistration registration)
    {
        _robot = registration;
        _devices[registration.DeviceId] = registration;
        _robotProfile = new RobotProfile
        {
            RobotId = registration.RobotId,
            Payload = new Dictionary<string, object?>
            {
                ["SSID"] = "my-ssid",
                ["connectedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["platform"] = registration.FirmwareVersion ?? "12.10.0",
                ["serialNumber"] = registration.DeviceId
            },
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        TouchState();
    }

    private void TouchState()
    {
        Interlocked.Increment(ref _revision);
        SavePersistedState();
    }

    private static string ResolveDefaultLoopId(IReadOnlyList<LoopRecord> loops, AccountProfile account)
    {
        return loops.FirstOrDefault(loop => string.Equals(loop.OwnerAccountId, account.AccountId, StringComparison.OrdinalIgnoreCase))?.LoopId
               ?? loops.FirstOrDefault()?.LoopId
               ?? "openjibo-default-loop";
    }

    private string ResolveDefaultLoopId()
    {
        return ResolveDefaultLoopId(_loops, _account);
    }

    private static IDictionary<string, object?> BuildSessionMetadata(string accountId, string? deviceId, string loopId)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["accountId"] = accountId,
            ["loopId"] = loopId,
            ["deviceId"] = deviceId
        };
    }

    private static CloudSessionSnapshot MapSessionSnapshot(CloudSession session)
    {
        return new CloudSessionSnapshot
        {
            SessionId = session.SessionId,
            Kind = session.Kind,
            AccountId = session.AccountId,
            DeviceId = session.DeviceId,
            Token = session.Token,
            HostName = session.HostName,
            Path = session.Path,
            CreatedUtc = session.CreatedUtc,
            LastSeenUtc = session.LastSeenUtc,
            FollowUpExpiresUtc = session.FollowUpExpiresUtc,
            LastMessageType = session.LastMessageType,
            LastListenType = session.LastListenType,
            LastIntent = session.LastIntent,
            LastTranscript = session.LastTranscript,
            LastTransId = session.LastTransId,
            Metadata = session.Metadata
        };
    }

    private const string CurrentSchemaVersion = "1";

    private sealed class PersistentStateSnapshot
    {
        public string SchemaVersion { get; init; } = CurrentSchemaVersion;
        public long Revision { get; init; }
        public DateTimeOffset? LastLoadedUtc { get; init; }
        public DateTimeOffset? LastSavedUtc { get; init; }
        public AccountProfile? Account { get; init; }
        public DeviceRegistration? Robot { get; init; }
        public RobotProfile? RobotProfile { get; init; }
        public DeviceRegistration[]? Devices { get; init; }
        public CloudSessionSnapshot[]? Sessions { get; init; }
        public Dictionary<string, string>? SymmetricKeys { get; init; }
        public KeyRequestRecord[]? KeyRequests { get; init; }
        public UpdateManifest[]? Updates { get; init; }
        public MediaRecord[]? Media { get; init; }
        public BackupRecord[]? Backups { get; init; }
        public LoopRecord[]? Loops { get; init; }
        public PersonRecord[]? People { get; init; }
    }

    private sealed class CloudSessionSnapshot
    {
        public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
        public string Kind { get; init; } = "http";
        public string? AccountId { get; init; }
        public string? DeviceId { get; init; }
        public string? Token { get; init; }
        public string? HostName { get; init; }
        public string? Path { get; init; }
        public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastSeenUtc { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? FollowUpExpiresUtc { get; init; }
        public string? LastMessageType { get; init; }
        public string? LastListenType { get; init; }
        public string? LastIntent { get; init; }
        public string? LastTranscript { get; init; }
        public string? LastTransId { get; init; }
        public IDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();

        public CloudSession ToRecord()
        {
            return new CloudSession
            {
                SessionId = SessionId,
                Kind = Kind,
                AccountId = AccountId,
                DeviceId = DeviceId,
                Token = Token,
                HostName = HostName,
                Path = Path,
                CreatedUtc = CreatedUtc,
                LastSeenUtc = LastSeenUtc,
                FollowUpExpiresUtc = FollowUpExpiresUtc,
                LastMessageType = LastMessageType,
                LastListenType = LastListenType,
                LastIntent = LastIntent,
                LastTranscript = LastTranscript,
                LastTransId = LastTransId,
                Metadata = new Dictionary<string, object?>(Metadata, StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
