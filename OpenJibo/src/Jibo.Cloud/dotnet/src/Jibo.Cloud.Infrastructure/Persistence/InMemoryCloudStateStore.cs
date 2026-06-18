using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Holidays;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class InMemoryCloudStateStore : ICloudStateStore
{
    private const string CurrentSchemaVersion = "1";

    private static readonly JsonSerializerOptions PersistenceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly List<BackupRecord> _backups = [];
    private readonly List<CalendarEventRecord> _calendarEvents = [];
    private readonly List<CommuteProfileRecord> _commuteProfiles = [];
    private readonly ConcurrentDictionary<string, DeviceRegistration> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GreetingPresenceRecord> _greetingPresences = [];

    private readonly IHolidayCalendarProvider _holidayCalendarProvider;
    private readonly List<HolidayRecord> _holidayOverrides = [];

    private readonly ConcurrentDictionary<string, KeyRequestRecord>
        _keyRequests = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<LoopMemberRecord> _loopMembers;

    private readonly List<LoopRecord> _loops;
    private readonly List<MediaRecord> _media = [];
    private readonly string? _ownerFirstName;
    private readonly string? _ownerLastName;
    private readonly List<PersonRecord> _people;

    private readonly ConcurrentDictionary<string, CloudSession>
        _sessionsByToken = new(StringComparer.OrdinalIgnoreCase);

    private readonly ISnapshotStore _snapshotStore;
    private readonly ConcurrentDictionary<string, string> _symmetricKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _syncRoot = new();
    private readonly List<UpdateManifest> _updates;
    private readonly List<UserRecord> _users;

    private AccountProfile _account = new();
    private DateTimeOffset? _lastLoadedUtc;
    private DateTimeOffset? _lastSavedUtc;
    private long _revision;
    private DeviceRegistration _robot;
    private RobotProfile _robotProfile;

    public InMemoryCloudStateStore(string? persistencePath = null)
        : this(new JsonFileSnapshotStore(persistencePath, PersistenceJsonOptions))
    {
    }

    public InMemoryCloudStateStore(ISnapshotStore snapshotStore)
        : this(snapshotStore, new NagerDateHolidayCalendarProvider())
    {
    }

    public InMemoryCloudStateStore(ISnapshotStore snapshotStore, IHolidayCalendarProvider holidayCalendarProvider,
        string? ownerFirstName = null, string? ownerLastName = null)
    {
        _snapshotStore = snapshotStore;
        _holidayCalendarProvider = holidayCalendarProvider;
        _ownerFirstName = ownerFirstName;
        _ownerLastName = ownerLastName;
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
        _loopMembers = [];
        _users = [];
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
        ApplyConfiguredOwnerName();
        EnsureDefaultTopology();
        LoadPersistedState();
    }

    public PersistenceStateInfo GetPersistenceStateInfo()
    {
        return new PersistenceStateInfo(
            CurrentSchemaVersion,
            Interlocked.Read(ref _revision),
            _lastLoadedUtc,
            _lastSavedUtc);
    }

    public void LoadPersistedState()
    {
        var snapshot = _snapshotStore.Load<PersistentStateSnapshot>();
        if (snapshot is null) return;
        ApplySnapshot(snapshot);
    }

    public void SavePersistedState()
    {
        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var snapshot = CaptureSnapshot(now);
            _snapshotStore.Save(snapshot);
            _lastSavedUtc = now;
        }
    }

    public AccountProfile GetAccount()
    {
        return _account;
    }

    public DeviceRegistration GetRobot()
    {
        return _robot;
    }

    public RobotProfile GetRobotProfile()
    {
        return _robotProfile;
    }

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

    public UserRecord? CreateUser(string email, string password, string? firstName, string? lastName)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return null;

        lock (_syncRoot)
        {
            if (_users.Any(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
                return null;

            var salt = GenerateSalt();
            var user = new UserRecord
            {
                Email = email.Trim().ToLowerInvariant(),
                PasswordHash = HashPassword(password, salt),
                Salt = salt,
                FirstName = firstName?.Trim() ?? string.Empty,
                LastName = lastName?.Trim() ?? string.Empty
            };
            _users.Add(user);
        }

        TouchState();
        return _users.Last(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    }

    public UserRecord? AuthenticateUser(string email, string password)
    {
        var user = _users.FirstOrDefault(u => u.Email.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user is null) return null;

        var hash = HashPassword(password, user.Salt);
        return hash == user.PasswordHash ? user : null;
    }

    public UserRecord? GetUserById(string id)
    {
        return _users.FirstOrDefault(u => u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public UserRecord? GetUserByEmail(string email)
    {
        return _users.FirstOrDefault(u => u.Email.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public UserRecord UpdateUser(string id, string? firstName, string? lastName, string? gender, long? birthday)
    {
        lock (_syncRoot)
        {
            var index = _users.FindIndex(u => u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException($"User '{id}' not found.");

            var existing = _users[index];
            _users[index] = new UserRecord
            {
                Id = existing.Id,
                Email = existing.Email,
                PasswordHash = existing.PasswordHash,
                Salt = existing.Salt,
                FirstName = firstName?.Trim() ?? existing.FirstName,
                LastName = lastName?.Trim() ?? existing.LastName,
                Gender = gender ?? existing.Gender,
                Birthday = birthday ?? existing.Birthday,
                AccessKeyId = existing.AccessKeyId,
                SecretAccessKey = existing.SecretAccessKey,
                IsActive = existing.IsActive,
                CreatedUtc = existing.CreatedUtc
            };
        }

        TouchState();
        return _users.First(u => u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
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

        if (string.IsNullOrWhiteSpace(token)) return session;

        _sessionsByToken[token] = session;
        TouchState();

        return session;
    }

    public CloudSession? FindSessionByToken(string token)
    {
        return _sessionsByToken.GetValueOrDefault(token);
    }

    public IReadOnlyList<LoopRecord> GetLoops()
    {
        return _loops.ToArray();
    }

    public IReadOnlyList<PersonRecord> GetPeople()
    {
        return _people.ToArray();
    }

    public IReadOnlyList<LoopMemberRecord> GetLoopMembers(string loopId)
    {
        return _loopMembers
            .Where(m => m.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                        !m.Status.Equals("removed", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public LoopMemberRecord AddLoopMember(string loopId, string? accountId, string? email, string? firstName,
        string? lastName, string? gender, long? birthday, bool isChild, string type)
    {
        var member = new LoopMemberRecord
        {
            LoopId = loopId,
            AccountId = accountId,
            Email = email?.Trim(),
            FirstName = firstName?.Trim(),
            LastName = lastName?.Trim(),
            Gender = gender,
            Birthday = birthday,
            IsChild = isChild,
            Type = type,
            Status = "active"
        };
        lock (_syncRoot)
        {
            _loopMembers.Add(member);
        }

        TouchState();
        return member;
    }

    public LoopMemberRecord UpdateLoopMember(string loopId, string memberId, string? firstName, string? lastName,
        string? gender, long? birthday, bool isChild, string? nickname, string? phoneticName)
    {
        lock (_syncRoot)
        {
            var index = _loopMembers.FindIndex(m =>
                m.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                m.Id.Equals(memberId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException($"Member '{memberId}' not found in loop '{loopId}'.");

            var existing = _loopMembers[index];
            _loopMembers[index] = new LoopMemberRecord
            {
                Id = existing.Id,
                LoopId = existing.LoopId,
                AccountId = existing.AccountId,
                Email = existing.Email,
                FirstName = firstName?.Trim() ?? existing.FirstName,
                LastName = lastName?.Trim() ?? existing.LastName,
                Gender = gender ?? existing.Gender,
                Birthday = birthday ?? existing.Birthday,
                IsChild = isChild,
                PhoneNumber = existing.PhoneNumber,
                Status = existing.Status,
                Type = existing.Type,
                Nickname = nickname ?? existing.Nickname,
                PhoneticName = phoneticName ?? existing.PhoneticName,
                FaceEnrolled = existing.FaceEnrolled,
                VoiceEnrolled = existing.VoiceEnrolled,
                LegalGuardianId = existing.LegalGuardianId,
                AgreementId = existing.AgreementId,
                CreatedUtc = existing.CreatedUtc
            };
        }

        TouchState();
        return GetLoopMember(loopId, memberId);
    }

    public bool RemoveLoopMember(string loopId, string memberId)
    {
        lock (_syncRoot)
        {
            var index = _loopMembers.FindIndex(m =>
                m.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                m.Id.Equals(memberId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;

            var existing = _loopMembers[index];
            _loopMembers[index] = new LoopMemberRecord
            {
                Id = existing.Id,
                LoopId = existing.LoopId,
                AccountId = existing.AccountId,
                Email = existing.Email,
                FirstName = existing.FirstName,
                LastName = existing.LastName,
                Gender = existing.Gender,
                Birthday = existing.Birthday,
                IsChild = existing.IsChild,
                PhoneNumber = existing.PhoneNumber,
                Status = "removed",
                Type = existing.Type,
                Nickname = existing.Nickname,
                PhoneticName = existing.PhoneticName,
                FaceEnrolled = existing.FaceEnrolled,
                VoiceEnrolled = existing.VoiceEnrolled,
                LegalGuardianId = existing.LegalGuardianId,
                AgreementId = existing.AgreementId,
                CreatedUtc = existing.CreatedUtc
            };
        }

        TouchState();
        return true;
    }

    public LoopMemberRecord SetMemberEnrollment(string loopId, string memberId, bool? face, bool? voice)
    {
        lock (_syncRoot)
        {
            var index = _loopMembers.FindIndex(m =>
                m.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                m.Id.Equals(memberId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException($"Member '{memberId}' not found in loop '{loopId}'.");

            var existing = _loopMembers[index];
            _loopMembers[index] = new LoopMemberRecord
            {
                Id = existing.Id,
                LoopId = existing.LoopId,
                AccountId = existing.AccountId,
                Email = existing.Email,
                FirstName = existing.FirstName,
                LastName = existing.LastName,
                Gender = existing.Gender,
                Birthday = existing.Birthday,
                IsChild = existing.IsChild,
                PhoneNumber = existing.PhoneNumber,
                Status = existing.Status,
                Type = existing.Type,
                Nickname = existing.Nickname,
                PhoneticName = existing.PhoneticName,
                FaceEnrolled = face ?? existing.FaceEnrolled,
                VoiceEnrolled = voice ?? existing.VoiceEnrolled,
                LegalGuardianId = existing.LegalGuardianId,
                AgreementId = existing.AgreementId,
                CreatedUtc = existing.CreatedUtc
            };
        }

        TouchState();
        return GetLoopMember(loopId, memberId);
    }

    public IReadOnlyList<UpdateManifest> ListUpdates(string? subsystem = null, string? filter = null)
    {
        return _updates
            .Where(update =>
                subsystem is null || update.Subsystem.Equals(subsystem, StringComparison.OrdinalIgnoreCase))
            .Where(update => filter is null || string.Equals(update.Filter, filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public UpdateManifest? GetUpdateFrom(string? subsystem, string? fromVersion, string? filter)
    {
        return ListUpdates(subsystem, filter)
            .FirstOrDefault(update =>
                fromVersion is null || update.FromVersion.Equals(fromVersion, StringComparison.OrdinalIgnoreCase));
    }

    public UpdateManifest CreateUpdate(string? fromVersion, string? toVersion, string? changes, string? shaHash,
        long? length, string? subsystem, string? filter, IDictionary<string, object?>? dependencies)
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
            return new UpdateManifest
            {
                UpdateId = updateId ?? "unknown-update",
                Changes = "Update not found",
                Url = "https://api.jibo.com/update/missing",
                ShaHash = "missing",
                Subsystem = "unknown"
            };

        _updates.Remove(existing);
        TouchState();
        return existing;
    }

    public IReadOnlyList<MediaRecord> ListMedia(IReadOnlyList<string>? loopIds = null, long? after = null,
        long? before = null)
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
            if (!paths.Contains(_media[i].Path)) continue;

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

        if (replacements.Count > 0) TouchState();

        return replacements;
    }

    public MediaRecord CreateMedia(string loopId, string path, string type, string reference, bool isEncrypted,
        IDictionary<string, object?>? meta)
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

        var existingIndex =
            _media.FindIndex(existing => existing.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            _media[existingIndex] = item;
        else
            _media.Add(item);

        TouchState();
        return item;
    }

    public IReadOnlyList<BackupRecord> GetBackups()
    {
        return _backups.ToArray();
    }

    public BackupRecord CreateBackup(string loopId, string name)
    {
        var snapshotJson = JsonSerializer.Serialize(CaptureSnapshot(DateTimeOffset.UtcNow), PersistenceJsonOptions);
        var backup = new BackupRecord
        {
            LoopId = string.IsNullOrWhiteSpace(loopId) ? null : loopId.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? "backup" : name.Trim(),
            SnapshotJson = snapshotJson
        };

        _backups.Add(backup);
        TouchState();
        return backup;
    }

    public BackupRecord? RestoreBackup(string? backupId = null)
    {
        BackupRecord? backup;
        lock (_syncRoot)
        {
            backup = string.IsNullOrWhiteSpace(backupId)
                ? _backups.OrderByDescending(item => item.CreatedUtc).FirstOrDefault()
                : _backups.FirstOrDefault(item => item.BackupId.Equals(backupId, StringComparison.OrdinalIgnoreCase));

            if (backup is null || string.IsNullOrWhiteSpace(backup.SnapshotJson))
                return null;

            PersistentStateSnapshot? snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<PersistentStateSnapshot>(backup.SnapshotJson,
                    PersistenceJsonOptions);
            }
            catch
            {
                snapshot = null;
            }

            if (snapshot is null)
                return null;

            ApplySnapshot(snapshot);
            Interlocked.Increment(ref _revision);
        }

        SavePersistedState();
        return backup;
    }

    public IReadOnlyList<CalendarEventRecord> GetCalendarEvents(string? loopId = null)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(loopId) ? ResolveDefaultLoopId() : loopId.Trim();
        return _calendarEvents
            .Where(calendarEvent => calendarEvent.IsEnabled)
            .Where(calendarEvent =>
                string.Equals(calendarEvent.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(calendarEvent => calendarEvent.Date)
            .ThenBy(calendarEvent => calendarEvent.TimeLabel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(calendarEvent => calendarEvent.Summary, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public CalendarEventRecord UpsertCalendarEvent(CalendarEventRecord calendarEvent)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(calendarEvent.LoopId)
            ? ResolveDefaultLoopId()
            : calendarEvent.LoopId.Trim();
        var normalizedId = string.IsNullOrWhiteSpace(calendarEvent.Id)
            ? $"calendar-{resolvedLoopId}-{Slugify(calendarEvent.Summary)}"
            : calendarEvent.Id.Trim();

        var resolvedCalendarEvent = new CalendarEventRecord
        {
            Id = normalizedId,
            LoopId = resolvedLoopId,
            Summary =
                string.IsNullOrWhiteSpace(calendarEvent.Summary) ? "Calendar event" : calendarEvent.Summary.Trim(),
            TimeLabel = string.IsNullOrWhiteSpace(calendarEvent.TimeLabel) ? null : calendarEvent.TimeLabel.Trim(),
            Date = calendarEvent.Date,
            EndDate = calendarEvent.EndDate,
            IsAllDay = calendarEvent.IsAllDay,
            IsEnabled = calendarEvent.IsEnabled,
            Source = string.IsNullOrWhiteSpace(calendarEvent.Source) ? "manual" : calendarEvent.Source.Trim(),
            MemberId = calendarEvent.MemberId,
            Created = calendarEvent.Created
        };

        var existingIndex = _calendarEvents.FindIndex(existing =>
            string.Equals(existing.Id, normalizedId, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
            _calendarEvents[existingIndex] = resolvedCalendarEvent;
        else
            _calendarEvents.Add(resolvedCalendarEvent);

        TouchState();
        return resolvedCalendarEvent;
    }

    public IReadOnlyList<GreetingPresenceRecord> GetGreetingPresences(string? loopId = null)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(loopId) ? ResolveDefaultLoopId() : loopId.Trim();
        return _greetingPresences
            .Where(greeting =>
                string.Equals(greeting.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static greeting => greeting.LastGreetedUtc ?? greeting.LastSeenUtc)
            .ThenByDescending(static greeting => greeting.UpdatedUtc)
            .ToArray();
    }

    public GreetingPresenceRecord UpsertGreetingPresence(GreetingPresenceRecord greetingPresence)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(greetingPresence.LoopId)
            ? ResolveDefaultLoopId()
            : greetingPresence.LoopId.Trim();
        var resolvedPersonId = string.IsNullOrWhiteSpace(greetingPresence.PersonId)
            ? greetingPresence.SpeakerId?.Trim() ?? "unknown-person"
            : greetingPresence.PersonId.Trim();
        var normalizedId = string.IsNullOrWhiteSpace(greetingPresence.Id)
            ? $"greeting-presence-{resolvedLoopId}-{Slugify(resolvedPersonId)}"
            : greetingPresence.Id.Trim();
        var now = DateTimeOffset.UtcNow;
        var resolvedPresence = new GreetingPresenceRecord
        {
            Id = normalizedId,
            AccountId = string.IsNullOrWhiteSpace(greetingPresence.AccountId)
                ? _account.AccountId
                : greetingPresence.AccountId.Trim(),
            LoopId = resolvedLoopId,
            PersonId = resolvedPersonId,
            SpeakerId =
                string.IsNullOrWhiteSpace(greetingPresence.SpeakerId) ? null : greetingPresence.SpeakerId.Trim(),
            PreferredName = string.IsNullOrWhiteSpace(greetingPresence.PreferredName)
                ? null
                : greetingPresence.PreferredName.Trim(),
            LastSeenUtc = greetingPresence.LastSeenUtc == default ? now : greetingPresence.LastSeenUtc,
            LastGreetedUtc = greetingPresence.LastGreetedUtc,
            LastGreetingRoute = string.IsNullOrWhiteSpace(greetingPresence.LastGreetingRoute)
                ? null
                : greetingPresence.LastGreetingRoute.Trim(),
            LastGreetingIntent = string.IsNullOrWhiteSpace(greetingPresence.LastGreetingIntent)
                ? null
                : greetingPresence.LastGreetingIntent.Trim(),
            CreatedUtc = greetingPresence.CreatedUtc == default ? now : greetingPresence.CreatedUtc,
            UpdatedUtc = now
        };

        var existingIndex = _greetingPresences.FindIndex(existing =>
            string.Equals(existing.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.PersonId, resolvedPersonId, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
            _greetingPresences[existingIndex] = resolvedPresence;
        else
            _greetingPresences.Add(resolvedPresence);

        TouchState();
        return resolvedPresence;
    }

    public bool ShouldCreateSymmetricKey(string loopId)
    {
        return !_symmetricKeys.ContainsKey(loopId);
    }

    public string GetOrCreateSymmetricKey(string loopId)
    {
        if (_symmetricKeys.TryGetValue(loopId, out var existing)) return existing;

        var key = Convert.ToBase64String(Encoding.UTF8.GetBytes($"open-jibo-symmetric-key:{loopId}"));
        if (!_symmetricKeys.TryAdd(loopId, key)) return _symmetricKeys[loopId];

        TouchState();
        return key;
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
        if (!string.IsNullOrWhiteSpace(requestId) && _keyRequests.TryGetValue(requestId, out var record)) return record;

        return new KeyRequestRecord
        {
            RequestId = requestId ?? "unknown-request",
            LoopId = loopId,
            PublicKey = publicKey ?? string.Empty
        };
    }

    public IReadOnlyList<KeyRequestRecord> GetIncomingKeyRequests()
    {
        return [];
    }

    public IReadOnlyList<KeyRequestRecord> GetBinaryRequests()
    {
        return [];
    }

    public IReadOnlyList<HolidayRecord> GetHolidays(string? loopId = null)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(loopId) ? ResolveDefaultLoopId() : loopId.Trim();
        var years = new[] { DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Year + 1 };

        var systemHolidays = years
            .SelectMany(year => _holidayCalendarProvider.GetPublicHolidays(null, year))
            .Where(holiday => holiday.IsEnabled)
            .Select(holiday => new HolidayRecord
            {
                Id = holiday.Id,
                EventId = holiday.EventId,
                Name = holiday.Name,
                Category = holiday.Category,
                Subcategory = holiday.Subcategory,
                LoopId = resolvedLoopId,
                MemberId = holiday.MemberId,
                IsEnabled = true,
                Date = holiday.Date,
                EndDate = holiday.EndDate,
                Source = holiday.Source,
                CountryCode = holiday.CountryCode,
                Created = holiday.Created
            })
            .ToList();

        var overrides = _holidayOverrides
            .Where(holiday => string.Equals(holiday.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var overrideHoliday in overrides.Where(overrideHoliday =>
                     !string.IsNullOrWhiteSpace(overrideHoliday.EventId)))
            systemHolidays.RemoveAll(systemHoliday =>
                string.Equals(systemHoliday.EventId, overrideHoliday.EventId, StringComparison.OrdinalIgnoreCase));

        return systemHolidays
            .Concat(overrides.Where(holiday => holiday.IsEnabled))
            .OrderBy(holiday => holiday.Date)
            .ThenBy(holiday => holiday.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<CommuteProfileRecord> GetCommuteProfiles(string? loopId = null)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(loopId) ? ResolveDefaultLoopId() : loopId.Trim();
        return _commuteProfiles
            .Where(commute => commute.IsEnabled)
            .Where(commute => string.Equals(commute.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static commute => commute.IsComplete)
            .ThenByDescending(static commute => commute.Updated)
            .ToArray();
    }

    public CommuteProfileRecord UpsertCommuteProfile(CommuteProfileRecord commuteProfile)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(commuteProfile.LoopId)
            ? ResolveDefaultLoopId()
            : commuteProfile.LoopId.Trim();
        var normalizedId = string.IsNullOrWhiteSpace(commuteProfile.Id)
            ? $"commute-{resolvedLoopId}"
            : commuteProfile.Id.Trim();

        var resolvedProfile = new CommuteProfileRecord
        {
            Id = normalizedId,
            LoopId = resolvedLoopId,
            MemberId = string.IsNullOrWhiteSpace(commuteProfile.MemberId) ? null : commuteProfile.MemberId.Trim(),
            IsEnabled = commuteProfile.IsEnabled,
            IsComplete = commuteProfile.IsComplete,
            Mode = string.IsNullOrWhiteSpace(commuteProfile.Mode) ? "driving" : commuteProfile.Mode.Trim(),
            WorkHour = commuteProfile.WorkHour,
            WorkMinute = commuteProfile.WorkMinute,
            OriginName = string.IsNullOrWhiteSpace(commuteProfile.OriginName) ? null : commuteProfile.OriginName.Trim(),
            DestinationName = string.IsNullOrWhiteSpace(commuteProfile.DestinationName)
                ? null
                : commuteProfile.DestinationName.Trim(),
            TypicalDurationMinutes = commuteProfile.TypicalDurationMinutes > 0
                ? commuteProfile.TypicalDurationMinutes
                : 25,
            Created = commuteProfile.Created == default ? DateTimeOffset.UtcNow : commuteProfile.Created,
            Updated = DateTimeOffset.UtcNow
        };

        var existingIndex = _commuteProfiles.FindIndex(existing =>
            string.Equals(existing.Id, normalizedId, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
            _commuteProfiles[existingIndex] = resolvedProfile;
        else
            _commuteProfiles.Add(resolvedProfile);

        TouchState();
        return resolvedProfile;
    }

    public HolidayRecord UpsertHoliday(HolidayRecord holiday)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(holiday.LoopId) ? ResolveDefaultLoopId() : holiday.LoopId.Trim();
        var normalizedEventId = string.IsNullOrWhiteSpace(holiday.EventId)
            ? $"holiday-{resolvedLoopId}-{Slugify(holiday.Name)}"
            : holiday.EventId.Trim();
        var normalizedId = string.IsNullOrWhiteSpace(holiday.Id) ? normalizedEventId : holiday.Id.Trim();
        var resolvedHoliday = new HolidayRecord
        {
            Id = normalizedId,
            EventId = normalizedEventId,
            Name = string.IsNullOrWhiteSpace(holiday.Name) ? "Holiday" : holiday.Name.Trim(),
            Category = string.IsNullOrWhiteSpace(holiday.Category) ? "holiday" : holiday.Category.Trim(),
            Subcategory = holiday.Subcategory,
            LoopId = resolvedLoopId,
            MemberId = holiday.MemberId,
            IsEnabled = holiday.IsEnabled,
            Date = holiday.Date,
            EndDate = holiday.EndDate,
            Source = string.IsNullOrWhiteSpace(holiday.Source) ? "manual" : holiday.Source.Trim(),
            CountryCode = string.IsNullOrWhiteSpace(holiday.CountryCode) ? "US" : holiday.CountryCode.Trim(),
            Created = holiday.Created
        };

        var existingIndex = _holidayOverrides.FindIndex(existing =>
            string.Equals(existing.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.EventId, normalizedEventId, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
            _holidayOverrides[existingIndex] = resolvedHoliday;
        else
            _holidayOverrides.Add(resolvedHoliday);

        TouchState();
        return resolvedHoliday;
    }

    public void UpdateRobot(DeviceRegistration registration)
    {
        var oldRobotId = _robot.RobotId;
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

        for (var i = 0; i < _loopMembers.Count; i++)
        {
            var member = _loopMembers[i];
            if (string.Equals(member.Type, "robot", StringComparison.OrdinalIgnoreCase) ||
                (member.AccountId != null && member.AccountId.Equals(oldRobotId, StringComparison.OrdinalIgnoreCase)))
                _loopMembers[i] = new LoopMemberRecord
                {
                    Id = member.Id,
                    LoopId = member.LoopId,
                    AccountId = string.Equals(member.AccountId, oldRobotId, StringComparison.OrdinalIgnoreCase)
                        ? registration.RobotId
                        : member.AccountId,
                    Email = member.Email,
                    FirstName = member.FirstName,
                    LastName = member.LastName,
                    Gender = member.Gender,
                    Birthday = member.Birthday,
                    IsChild = member.IsChild,
                    PhoneNumber = member.PhoneNumber,
                    Status = member.Status,
                    Type = member.Type,
                    Nickname = member.Nickname,
                    PhoneticName = member.PhoneticName,
                    FaceEnrolled = member.FaceEnrolled,
                    VoiceEnrolled = member.VoiceEnrolled,
                    LegalGuardianId = member.LegalGuardianId,
                    AgreementId = member.AgreementId,
                    CreatedUtc = member.CreatedUtc
                };
        }

        if (RobotFriendlyNameValidator.TryNormalize(registration.DeviceId, out var normalizedDeviceId, out _))
        {
            foreach (var session in _sessionsByToken.Values)
            {
                if (!string.Equals(session.Kind, "hub", StringComparison.OrdinalIgnoreCase)) continue;
                session.Metadata["robotFriendlyName"] = normalizedDeviceId;
            }
        }

        TouchState();
    }

    private static bool IsUpdateNewerThanRequest(string candidateVersion, string? fromVersion)
    {
        if (string.IsNullOrWhiteSpace(fromVersion)) return true;

        if (string.IsNullOrWhiteSpace(candidateVersion)) return false;

        if (Version.TryParse(candidateVersion, out var candidate) &&
            Version.TryParse(fromVersion, out var requested))
            return candidate > requested;

        return string.Compare(candidateVersion, fromVersion, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static string GenerateSalt()
    {
        Span<byte> saltBytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(saltBytes);
        return Convert.ToHexString(saltBytes).ToLowerInvariant();
    }

    private static string HashPassword(string password, string salt)
    {
        var input = Encoding.UTF8.GetBytes($"{salt}:{password}");
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    private void TouchState()
    {
        Interlocked.Increment(ref _revision);
        SavePersistedState();
    }

    private void ApplyConfiguredOwnerName()
    {
        if (string.IsNullOrWhiteSpace(_ownerFirstName) && string.IsNullOrWhiteSpace(_ownerLastName))
            return;

        _account = new AccountProfile
        {
            AccountId = _account.AccountId,
            Email = _account.Email,
            FirstName = !string.IsNullOrWhiteSpace(_ownerFirstName) ? _ownerFirstName : _account.FirstName,
            LastName = !string.IsNullOrWhiteSpace(_ownerLastName) ? _ownerLastName : _account.LastName,
            AccessKeyId = _account.AccessKeyId,
            SecretAccessKey = _account.SecretAccessKey
        };

        for (var i = 0; i < _people.Count; i++)
        {
            var person = _people[i];
            if (!person.AccountId.Equals(_account.AccountId, StringComparison.OrdinalIgnoreCase)) continue;

            _people[i] = new PersonRecord
            {
                PersonId = person.PersonId,
                AccountId = person.AccountId,
                LoopId = person.LoopId,
                RobotId = person.RobotId,
                DisplayName = $"{_account.FirstName} {_account.LastName}".Trim(),
                Alias = _account.FirstName,
                IsPrimary = person.IsPrimary,
                CreatedUtc = person.CreatedUtc,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
        }

        for (var i = 0; i < _loopMembers.Count; i++)
        {
            var member = _loopMembers[i];
            if (member.AccountId == null ||
                !member.AccountId.Equals(_account.AccountId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member.Type, "robot", StringComparison.OrdinalIgnoreCase))
                continue;

            _loopMembers[i] = new LoopMemberRecord
            {
                Id = member.Id,
                LoopId = member.LoopId,
                AccountId = member.AccountId,
                Email = member.Email,
                FirstName = _account.FirstName,
                LastName = _account.LastName,
                Gender = member.Gender,
                Birthday = member.Birthday,
                IsChild = member.IsChild,
                PhoneNumber = member.PhoneNumber,
                Status = member.Status,
                Type = member.Type,
                Nickname = member.Nickname,
                PhoneticName = member.PhoneticName,
                FaceEnrolled = member.FaceEnrolled,
                VoiceEnrolled = member.VoiceEnrolled,
                LegalGuardianId = member.LegalGuardianId,
                AgreementId = member.AgreementId,
                CreatedUtc = member.CreatedUtc
            };
        }
    }

    private void EnsureDefaultTopology()
    {
        if (_loops.Count == 0)
            _loops.Add(new LoopRecord
            {
                OwnerAccountId = _account.AccountId,
                RobotId = _robot.RobotId,
                RobotFriendlyId = _robot.DeviceId
            });

        EnsureOwnerLoopMember(_loops[0].LoopId);
        EnsureRobotLoopMember(_loops[0].LoopId, _robot.RobotId);

        if (_people.Count != 0)
        {
            EnsureDefaultCommuteProfile();
            return;
        }

        var loopId = _loops[0].LoopId;
        _people.Add(new PersonRecord
        {
            PersonId = "person-openjibo-owner",
            AccountId = _account.AccountId,
            LoopId = loopId,
            RobotId = _robot.RobotId,
            DisplayName = $"{_account.FirstName} {_account.LastName}",
            Alias = _account.FirstName,
            IsPrimary = true
        });
        _people.Add(new PersonRecord
        {
            PersonId = "person-openjibo-household-member",
            AccountId = _account.AccountId,
            LoopId = loopId,
            RobotId = _robot.RobotId,
            DisplayName = "OpenJibo Household Member",
            Alias = "Household Member",
            IsPrimary = false
        });

        EnsureDefaultCommuteProfile();
    }

    private void EnsureOwnerLoopMember(string loopId)
    {
        if (_loopMembers.Any(member =>
                member.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                member.AccountId != null &&
                member.AccountId.Equals(_account.AccountId, StringComparison.OrdinalIgnoreCase) &&
                !member.Status.Equals("removed", StringComparison.OrdinalIgnoreCase)))
            return;

        _loopMembers.Add(new LoopMemberRecord
        {
            LoopId = loopId,
            AccountId = _account.AccountId,
            Email = _account.Email,
            FirstName = _account.FirstName,
            LastName = _account.LastName,
            Gender = "unknown",
            Birthday = 631152000000,
            Type = "owner",
            Status = "active"
        });
    }

    private void EnsureRobotLoopMember(string loopId, string robotId)
    {
        if (string.IsNullOrWhiteSpace(robotId)) return;

        if (_loopMembers.Any(member =>
                member.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                member.AccountId != null &&
                member.AccountId.Equals(robotId, StringComparison.OrdinalIgnoreCase) &&
                !member.Status.Equals("removed", StringComparison.OrdinalIgnoreCase)))
            return;

        _loopMembers.Add(new LoopMemberRecord
        {
            LoopId = loopId,
            AccountId = robotId,
            FirstName = "Jibo",
            LastName = "Robot",
            Gender = "unknown",
            Type = "robot",
            Status = "active"
        });
    }

    private void EnsureDefaultCommuteProfile()
    {
        if (_commuteProfiles.Any(commute =>
                string.Equals(commute.LoopId, ResolveDefaultLoopId(), StringComparison.OrdinalIgnoreCase)))
            return;

        _commuteProfiles.Add(new CommuteProfileRecord
        {
            Id = $"commute-{ResolveDefaultLoopId()}",
            LoopId = ResolveDefaultLoopId(),
            IsEnabled = true,
            IsComplete = true,
            Mode = "driving",
            WorkHour = 8,
            WorkMinute = 30,
            OriginName = "home",
            DestinationName = "work",
            TypicalDurationMinutes = 25
        });
    }

    private LoopMemberRecord GetLoopMember(string loopId, string memberId)
    {
        return _loopMembers.First(m =>
            m.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
            m.Id.Equals(memberId, StringComparison.OrdinalIgnoreCase));
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasDash = false;
                continue;
            }

            if (lastWasDash) continue;

            builder.Append('-');
            lastWasDash = true;
        }

        return builder.ToString().Trim('-');
    }

    private static string ResolveDefaultLoopId(IReadOnlyList<LoopRecord> loops, AccountProfile account)
    {
        return loops.FirstOrDefault(loop =>
                   string.Equals(loop.OwnerAccountId, account.AccountId, StringComparison.OrdinalIgnoreCase))?.LoopId
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

    private PersistentStateSnapshot CaptureSnapshot(DateTimeOffset now)
    {
        return new PersistentStateSnapshot
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
            SymmetricKeys = _symmetricKeys.ToDictionary(entry => entry.Key, entry => entry.Value,
                StringComparer.OrdinalIgnoreCase),
            KeyRequests = _keyRequests.Values.ToArray(),
            Updates = _updates.ToArray(),
            Media = _media.ToArray(),
            Backups = _backups.ToArray(),
            CommuteProfiles = _commuteProfiles.ToArray(),
            CalendarEvents = _calendarEvents.ToArray(),
            GreetingPresences = _greetingPresences.ToArray(),
            Loops = _loops.ToArray(),
            Holidays = _holidayOverrides.ToArray(),
            LoopMembers = _loopMembers.ToArray(),
            People = _people.ToArray(),
            Users = _users.ToArray()
        };
    }

    private void ApplySnapshot(PersistentStateSnapshot snapshot)
    {
        _account = snapshot.Account ?? _account;
        _robot = snapshot.Robot ?? _robot;
        _robotProfile = snapshot.RobotProfile ?? _robotProfile;

        _devices.Clear();
        foreach (var device in snapshot.Devices ?? []) _devices[device.DeviceId] = device;

        if (_devices.IsEmpty || !_devices.ContainsKey(_robot.DeviceId)) _devices[_robot.DeviceId] = _robot;

        _sessionsByToken.Clear();
        foreach (var session in snapshot.Sessions ?? [])
            if (!string.IsNullOrWhiteSpace(session.Token))
                _sessionsByToken[session.Token] = session.ToRecord();

        _symmetricKeys.Clear();
        foreach (var pair in snapshot.SymmetricKeys ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            _symmetricKeys[pair.Key] = pair.Value;

        _keyRequests.Clear();
        foreach (var keyRequest in snapshot.KeyRequests ?? []) _keyRequests[keyRequest.RequestId] = keyRequest;

        _updates.Clear();
        _updates.AddRange(snapshot.Updates ?? []);

        _media.Clear();
        _media.AddRange(snapshot.Media ?? []);

        _backups.Clear();
        _backups.AddRange(snapshot.Backups ?? []);

        _commuteProfiles.Clear();
        _commuteProfiles.AddRange(snapshot.CommuteProfiles ?? []);

        _calendarEvents.Clear();
        _calendarEvents.AddRange(snapshot.CalendarEvents ?? []);

        _greetingPresences.Clear();
        _greetingPresences.AddRange(snapshot.GreetingPresences ?? []);

        _loops.Clear();
        _loops.AddRange(snapshot.Loops ?? []);

        _holidayOverrides.Clear();
        _holidayOverrides.AddRange(snapshot.Holidays ?? []);

        _loopMembers.Clear();
        _loopMembers.AddRange(snapshot.LoopMembers ?? []);

        _people.Clear();
        _people.AddRange(snapshot.People ?? []);

        _users.Clear();
        _users.AddRange(snapshot.Users ?? []);

        ApplyConfiguredOwnerName();
        EnsureDefaultTopology();

        if (_robotProfile is null ||
            !string.Equals(_robotProfile.RobotId, _robot.RobotId, StringComparison.OrdinalIgnoreCase))
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

        Interlocked.Exchange(ref _revision, snapshot.Revision);
        _lastLoadedUtc = snapshot.LastLoadedUtc ?? DateTimeOffset.UtcNow;
        _lastSavedUtc = snapshot.LastSavedUtc;
    }

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
        public CommuteProfileRecord[]? CommuteProfiles { get; init; }
        public CalendarEventRecord[]? CalendarEvents { get; init; }
        public GreetingPresenceRecord[]? GreetingPresences { get; init; }
        public LoopRecord[]? Loops { get; init; }
        public HolidayRecord[]? Holidays { get; init; }
        public LoopMemberRecord[]? LoopMembers { get; init; }
        public PersonRecord[]? People { get; init; }
        public UserRecord[]? Users { get; init; }
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