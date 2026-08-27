using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

/// <summary>
/// Compatibility facade over normalized, scoped PostgreSQL repositories and a bounded live-session registry.
/// Relational writes commit immediately; this class never hydrates a fleet-wide persistence snapshot.
/// </summary>
public sealed partial class PostgreSqlCloudStateStore : ICloudStateStore
{
    private const string DefaultLoopId = "openjibo-default-loop";
    private readonly ICloudAccountRepository _accounts;
    private readonly ICloudAuthTokenRepository _authTokens;
    private readonly ICloudDeviceRepository _devices;
    private readonly IRobotIdentityLinkRepository _identityLinks;
    private readonly ILoopTopologyRepository? _loops;
    private readonly ILoopMemberRepository? _members;
    private readonly ICloudStateMetadataRepository _metadata;
    private readonly IPersonRepository? _people;
    private readonly IRecognitionObservationRepository? _recognition;
    private readonly IRobotProfileRepository? _robotProfiles;
    private readonly ICloudUserRepository? _users;
    private readonly ILoopKeyRepository? _loopKeys;
    private readonly IHolidayOverrideRepository? _holidays;
    private readonly ICommuteProfileRepository? _commutes;
    private readonly ICalendarEventRepository? _calendar;
    private readonly IGreetingPresenceRepository? _greetings;
    private readonly ITrustedServerRepository? _trustedServers;
    private readonly IUpdateManifestRepository? _updates;
    private readonly IMediaMetadataRepository? _media;
    private readonly IBackupManifestRepository? _backups;
    private readonly IBackupPayloadStore? _backupPayloads;
    private readonly IAtomicLoopBackupRestorer? _atomicBackupRestorer;
    private readonly ICloudStateSecretProtector? _secretProtector;
    private readonly BoundedCloudSessionRegistry _sessions;
    private readonly TimeSpan _hubTokenLifetime;
    private readonly TimeSpan _robotTokenLifetime;
    private readonly string? _ownerFirstName;
    private readonly string? _ownerLastName;
    private DateTimeOffset? _lastLoadedUtc;
    private DateTimeOffset? _lastSavedUtc;

    public PostgreSqlCloudStateStore(PostgreSqlCloudStateDataSource dataSource,
        ICloudStateSecretProtector secretProtector, int deviceCacheMaxEntries = 256,
        TimeSpan? deviceCacheTtl = null, int maximumActiveSessions = 256,
        TimeSpan? hubTokenLifetime = null, TimeSpan? robotTokenLifetime = null,
        IBackupPayloadStore? backupPayloadStore = null, string? ownerFirstName = null,
        string? ownerLastName = null, ITransportMetrics? transportMetrics = null)
        : this(
            new PostgreSqlCloudStateMetadataRepository(dataSource),
            new PostgreSqlCloudAccountRepository(dataSource, secretProtector),
            new PostgreSqlCloudDeviceRepository(dataSource, deviceCacheMaxEntries, deviceCacheTtl,
                transportMetrics: transportMetrics),
            new PostgreSqlCloudAuthTokenRepository(dataSource),
            new PostgreSqlRobotIdentityLinkRepository(dataSource),
            new BoundedCloudSessionRegistry(maximumActiveSessions, transportMetrics: transportMetrics),
            hubTokenLifetime, robotTokenLifetime,
            new PostgreSqlLoopTopologyRepository(dataSource),
            new PostgreSqlLoopMemberRepository(dataSource),
            new PostgreSqlPersonRepository(dataSource),
            new PostgreSqlRecognitionObservationRepository(dataSource),
            new PostgreSqlRobotProfileRepository(dataSource),
            new PostgreSqlCloudUserRepository(dataSource, secretProtector),
            new PostgreSqlLoopKeyRepository(dataSource), new PostgreSqlHolidayOverrideRepository(dataSource),
            new PostgreSqlCommuteProfileRepository(dataSource), new PostgreSqlCalendarEventRepository(dataSource),
            new PostgreSqlGreetingPresenceRepository(dataSource), new PostgreSqlTrustedServerRepository(dataSource),
            new PostgreSqlUpdateManifestRepository(dataSource), new PostgreSqlMediaMetadataRepository(dataSource),
            new PostgreSqlBackupManifestRepository(dataSource), backupPayloadStore,
            new PostgreSqlAtomicLoopBackupRestorer(dataSource),
            secretProtector,
            ownerFirstName, ownerLastName)
    {
        LoadPersistedState();
    }

    internal PostgreSqlCloudStateStore(
        ICloudStateMetadataRepository metadata,
        ICloudAccountRepository accounts,
        ICloudDeviceRepository devices,
        ICloudAuthTokenRepository authTokens,
        IRobotIdentityLinkRepository identityLinks,
        BoundedCloudSessionRegistry sessions, TimeSpan? hubTokenLifetime = null,
        TimeSpan? robotTokenLifetime = null,
        ILoopTopologyRepository? loops = null,
        ILoopMemberRepository? members = null,
        IPersonRepository? people = null,
        IRecognitionObservationRepository? recognition = null,
        IRobotProfileRepository? robotProfiles = null,
        ICloudUserRepository? users = null, ILoopKeyRepository? loopKeys = null,
        IHolidayOverrideRepository? holidays = null, ICommuteProfileRepository? commutes = null,
        ICalendarEventRepository? calendar = null, IGreetingPresenceRepository? greetings = null,
        ITrustedServerRepository? trustedServers = null, IUpdateManifestRepository? updates = null,
        IMediaMetadataRepository? media = null, IBackupManifestRepository? backups = null,
        IBackupPayloadStore? backupPayloads = null, IAtomicLoopBackupRestorer? atomicBackupRestorer = null,
        ICloudStateSecretProtector? secretProtector = null,
        string? ownerFirstName = null,
        string? ownerLastName = null)
    {
        _metadata = metadata;
        _accounts = accounts;
        _devices = devices;
        _authTokens = authTokens;
        _identityLinks = identityLinks;
        _sessions = sessions;
        _loops = loops;
        _members = members;
        _people = people;
        _recognition = recognition;
        _robotProfiles = robotProfiles;
        _users = users;
        _loopKeys = loopKeys;
        _holidays = holidays;
        _commutes = commutes;
        _calendar = calendar;
        _greetings = greetings;
        _trustedServers = trustedServers;
        _updates = updates;
        _media = media;
        _backups = backups;
        _backupPayloads = backupPayloads;
        _atomicBackupRestorer = atomicBackupRestorer;
        _secretProtector = secretProtector;
        _ownerFirstName = string.IsNullOrWhiteSpace(ownerFirstName) ? null : ownerFirstName.Trim();
        _ownerLastName = string.IsNullOrWhiteSpace(ownerLastName) ? null : ownerLastName.Trim();
        _hubTokenLifetime = PositiveLifetime(hubTokenLifetime, TimeSpan.FromDays(365));
        _robotTokenLifetime = PositiveLifetime(robotTokenLifetime, TimeSpan.FromDays(365));
    }

    public PersistenceStateInfo GetPersistenceStateInfo()
    {
        var state = Sync(_metadata.GetAsync());
        return new PersistenceStateInfo(state.SchemaVersion.ToString(), state.Revision, _lastLoadedUtc,
            _lastSavedUtc ?? state.UpdatedUtc);
    }

    public void LoadPersistedState()
    {
        _ = Sync(_metadata.GetAsync());
        EnsureDefaultTopology();
        if (_devices is PostgreSqlCloudDeviceRepository repository) repository.ClearCache();
        _sessions.Clear();
        _lastLoadedUtc = DateTimeOffset.UtcNow;
    }

    private void EnsureDefaultTopology()
    {
        var account = Sync(_accounts.GetDefaultAsync());
        if (account is null)
        {
            if (Sync(_metadata.HasLegacySnapshotAsync()))
                throw new InvalidOperationException(
                    "A legacy cloud-state snapshot exists but normalized state has not been imported. " +
                    "Run Jibo.Cloud.Migrations with --apply --target state --import-legacy-cloud-state before starting the API.");
            account = new AccountProfile
            {
                AccountId = "usr_openjibo_owner", Email = "owner@openjibo.local",
                FirstName = _ownerFirstName ?? "Jibo", LastName = _ownerLastName ?? "Owner",
                AccessKeyId = "openjibo-access-key", SecretAccessKey = "openjibo-secret-access-key"
            };
            Sync(_accounts.UpsertAsync(account, true));
        }

        var robot = Sync(_devices.GetDefaultAsync());
        if (robot is null)
        {
            robot = new DeviceRegistration
            {
                DeviceId = "openjibo-bootstrap-default", RobotId = "openjibo-bootstrap-default",
                FriendlyName = "OpenJibo Dev Robot", RegistrationSource = RobotRegistrationSources.Bootstrap,
                IsHidden = true, ArchivedUtc = DateTimeOffset.UtcNow,
                HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["api.jibo.com"] = "openjibo.com", ["api.openjibo.com"] = "openjibo.com",
                    ["api-socket.jibo.com"] = "openjibo.com",
                    ["open-jibo-socket.openjibo.com"] = "openjibo.com",
                    ["neo-hub.jibo.com"] = "openjibo.com", ["neohub.openjibo.com"] = "openjibo.com"
                }
            };
            Sync(_devices.UpsertAsync(robot, account.AccountId, true));
            if (_robotProfiles is not null)
                Sync(_robotProfiles.UpsertAsync(BuildDefaultRobotProfile(robot), robot.DeviceId));
        }

        if (_loops is null || _members is null || _people is null ||
            Sync(_loops.ListForAccountAsync(account.AccountId, 1)).Count > 0) return;
        var loop = AddLoop("OpenJibo Default Loop", account.AccountId, robot.RobotId, robot.DeviceId);
        var now = DateTimeOffset.UtcNow;
        Sync(_people.UpsertAsync(new PersonRecord
        {
            PersonId = "person-openjibo-owner", AccountId = account.AccountId, LoopId = loop.LoopId,
            RobotId = robot.RobotId, DisplayName = $"{account.FirstName} {account.LastName}".Trim(),
            Alias = account.FirstName, IsPrimary = true, CreatedUtc = now, UpdatedUtc = now
        }));
        Sync(_people.UpsertAsync(new PersonRecord
        {
            PersonId = "person-openjibo-household-member", AccountId = account.AccountId, LoopId = loop.LoopId,
            RobotId = robot.RobotId, DisplayName = "OpenJibo Household Member", Alias = "Household Member",
            CreatedUtc = now, UpdatedUtc = now
        }));
    }

    public void SavePersistedState()
    {
        // Scoped repository writes are committed immediately. Preserve the compatibility
        // method without issuing an unrelated database rewrite.
        _lastSavedUtc = Sync(_metadata.GetAsync()).UpdatedUtc;
    }

    public AccountProfile GetAccount() => Sync(_accounts.GetDefaultAsync()) ??
                                          throw MissingDefault("account");

    public UserRecord? CreateUser(string email, string password, string? firstName, string? lastName) =>
        Sync(RequireUsers().CreateAsync(email, password, firstName, lastName));

    public UserRecord? AuthenticateUser(string email, string password) =>
        Sync(RequireUsers().AuthenticateAsync(email, password));

    public UserRecord? GetUserById(string id) => Sync(RequireUsers().GetByIdAsync(id));

    public UserRecord? GetUserByEmail(string email) => Sync(RequireUsers().GetByEmailAsync(email));

    public UserRecord UpdateUser(string id, string? firstName, string? lastName, string? gender, long? birthday) =>
        Sync(RequireUsers().UpdateProfileAsync(id, firstName, lastName, gender, birthday));

    private ICloudUserRepository RequireUsers() => _users ??
        throw new InvalidOperationException("The normalized user repository is not configured.");

    public DeviceRegistration GetRobot() => Sync(_devices.GetDefaultAsync()) ??
                                            GetDevices().FirstOrDefault() ??
                                            throw MissingDefault("robot");

    public IReadOnlyList<DeviceRegistration> GetDevices()
    {
        var account = GetAccount();
        return Sync(_devices.ListForAccountAsync(account.AccountId, includeArchived: true));
    }

    public IReadOnlyList<DeviceRegistration> GetDevicesForAdministration() =>
        Sync(_devices.ListAllAsync(includeArchived: true));

    public IReadOnlyList<CloudSession> GetSessions() => _sessions.Values.ToArray();

    public RobotProfile GetRobotProfile()
    {
        var robot = GetRobot();
        return _robotProfiles is null ? BuildDefaultRobotProfile(robot) :
            Sync(_robotProfiles.GetAsync(robot.RobotId)) ?? BuildDefaultRobotProfile(robot);
    }

    private static RobotProfile BuildDefaultRobotProfile(DeviceRegistration robot) => new()
    {
        RobotId = robot.RobotId,
        Payload = new Dictionary<string, object?>
        {
            ["platform"] = robot.FirmwareVersion ?? "12.10.0",
            ["serialNumber"] = robot.DeviceId
        },
        UpdatedUtc = DateTimeOffset.UtcNow
    };

    public DeviceRegistration GetOrCreateDevice(string deviceId, string? firmwareVersion,
        string? applicationVersion, string? registrationSource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var existing = Sync(_devices.GetByDeviceIdAsync(deviceId));
        var accountId = GetAccount().AccountId;
        if (existing is not null)
        {
            if ((firmwareVersion is null || firmwareVersion == existing.FirmwareVersion) &&
                (applicationVersion is null || applicationVersion == existing.ApplicationVersion))
                return existing;

            return Sync(_devices.UpsertAsync(CloneDevice(existing,
                firmwareVersion: firmwareVersion ?? existing.FirmwareVersion,
                applicationVersion: applicationVersion ?? existing.ApplicationVersion), accountId));
        }

        var source = RobotRegistrationSources.Normalize(registrationSource, deviceId);
        var synthetic = RobotRegistrationSources.IsSynthetic(source);
        var created = new DeviceRegistration
        {
            DeviceId = deviceId.Trim(),
            RobotId = $"robot-{deviceId.Trim()}",
            FriendlyName = "OpenJibo Registered Robot",
            FirmwareVersion = firmwareVersion,
            ApplicationVersion = applicationVersion,
            RegistrationSource = source,
            IsHidden = synthetic,
            ArchivedUtc = synthetic ? DateTimeOffset.UtcNow : null
        };
        return Sync(_devices.UpsertAsync(created, accountId));
    }

    public DeviceRegistration UpsertDevice(DeviceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return Sync(_devices.UpsertAsync(registration, GetAccount().AccountId));
    }

    public DeviceRegistration UpsertDeviceForAdministration(DeviceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return Sync(_devices.UpsertAsync(registration));
    }

    public DeviceRegistration RenameDevice(string deviceId, string robotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(robotId);
        var existing = Sync(_devices.GetByDeviceIdAsync(deviceId)) ??
                       throw new KeyNotFoundException("Robot record was not found.");
        return Sync(_devices.UpsertAsync(CloneDevice(existing, robotId: robotId.Trim(),
            friendlyName: robotId.Trim()), GetAccount().AccountId));
    }

    public DeviceRegistration RenameDeviceForAdministration(string deviceId, string robotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(robotId);
        var existing = Sync(_devices.GetByDeviceIdAsync(deviceId)) ??
                       throw new KeyNotFoundException("Robot record was not found.");
        return Sync(_devices.UpsertAsync(CloneDevice(existing, robotId: robotId.Trim(),
            friendlyName: robotId.Trim())));
    }

    public DeviceRegistration? FindDeviceByFriendlyId(string friendlyId) =>
        Sync(_devices.FindByFriendlyIdAsync(friendlyId));

    public DeviceRegistration? FindDeviceByAwsCredentialFingerprint(string accessKeyFingerprint) =>
        Sync(_devices.FindByCredentialFingerprintAsync(accessKeyFingerprint));

    public IReadOnlyList<RobotCredentialBinding> GetRobotCredentialBindings() =>
        Sync(_devices.ListCredentialBindingsForAccountAsync(GetAccount().AccountId));

    public RobotCredentialBinding BindAwsCredentialFingerprint(string deviceId, string accessKeyFingerprint,
        string claimSource) => Sync(_devices.BindCredentialAsync(deviceId, accessKeyFingerprint, claimSource));

    public IReadOnlyList<RobotCredentialBinding> SwapAwsCredentialFingerprintBindings(
        string firstAccessKeyFingerprint, string secondAccessKeyFingerprint, string claimSource) =>
        Sync(_devices.SwapCredentialBindingsAsync(firstAccessKeyFingerprint, secondAccessKeyFingerprint,
            claimSource));

    public string IssueHubToken(string? deviceId = null, bool useDefaultRobot = true)
    {
        var account = GetAccount();
        var resolvedDeviceId = !string.IsNullOrWhiteSpace(deviceId)
            ? deviceId.Trim()
            : useDefaultRobot
                ? GetRobot().DeviceId
                : null;
        var token = $"hub-{account.AccountId}-{Guid.NewGuid():N}";
        RegisterIssuedToken(token, "hub", account.AccountId, resolvedDeviceId, _hubTokenLifetime);
        return token;
    }

    public string IssueRobotToken(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var device = Sync(_devices.GetByDeviceIdAsync(deviceId)) ??
                     throw new KeyNotFoundException("Robot record was not found.");
        var account = GetAccount();
        var token = $"token-{device.DeviceId}-{Guid.NewGuid():N}";
        RegisterIssuedToken(token, "robot", account.AccountId, device.DeviceId, _robotTokenLifetime);
        return token;
    }

    public CloudSession OpenSession(string kind, string? deviceId, string? token, string? hostName, string? path)
    {
        var sessionToken = string.IsNullOrWhiteSpace(token) ? $"conn:{Guid.NewGuid():N}" : token.Trim();
        var durableToken = _sessions.FindDurable(sessionToken);
        if (durableToken is null && !IsAmbiguousConnectionToken(sessionToken))
            durableToken = FindSessionByToken(sessionToken);
        var resolvedDeviceId = !string.IsNullOrWhiteSpace(deviceId)
            ? deviceId.Trim()
            : !string.IsNullOrWhiteSpace(durableToken?.DeviceId)
                ? durableToken.DeviceId
            : IsAmbiguousConnectionToken(sessionToken)
                ? null
                : GetRobot().DeviceId;
        var session = CreateSession(kind, durableToken?.AccountId ?? GetAccount().AccountId, resolvedDeviceId,
            sessionToken, hostName, path);
        if (durableToken is not null)
            foreach (var pair in durableToken.Metadata)
                session.Metadata[pair.Key] = pair.Value;
        _sessions.RegisterActive(sessionToken, session);
        ReinheritDialogMetadata(session);
        return session;
    }

    public void CloseSession(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId)) _sessions.RemoveBySessionId(sessionId);
    }

    public CloudSession? FindActiveSessionByToken(string token) =>
        string.IsNullOrWhiteSpace(token) ? null : _sessions.FindActive(token.Trim());

    public CloudSession? FindSessionByToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var key = token.Trim();
        var durable = _sessions.FindDurable(key);
        if (durable is null)
        {
            var stored = Sync(_authTokens.FindValidAsync(key));
            if (stored is null) return null;
            durable = CreateSession(stored.TokenKind, stored.AccountId, stored.DeviceId, key, null, null,
                stored.IssuedUtc);
            foreach (var pair in stored.Metadata) durable.Metadata[pair.Key] = pair.Value;
            _sessions.RegisterDurableToken(key, durable);
        }

        return durable;
    }

    public bool BindSessionToDevice(string sessionId, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(deviceId)) return false;
        var session = _sessions.Values.FirstOrDefault(candidate =>
            candidate.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase));
        var device = Sync(_devices.FindByFriendlyIdAsync(deviceId));
        if (session is null || device is null || device.IsHidden || device.ArchivedUtc is not null ||
            string.IsNullOrWhiteSpace(session.DeviceId)) return false;

        Sync(_identityLinks.UpsertAsync(session.DeviceId, device.DeviceId, "portal-admin"));
        ApplyRegisteredDeviceMetadata(session, device);
        return true;
    }

    public bool ClearSessionDeviceBinding(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false;
        var session = _sessions.Values.FirstOrDefault(candidate =>
            candidate.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase));
        if (session is null) return false;
        if (!string.IsNullOrWhiteSpace(session.DeviceId)) Sync(_identityLinks.RevokeAsync(session.DeviceId));

        foreach (var related in _sessions.Values.Where(candidate =>
                     string.Equals(candidate.DeviceId, session.DeviceId, StringComparison.OrdinalIgnoreCase)))
        {
            related.Metadata.Remove("registeredDeviceId");
            related.Metadata.Remove("registeredRobotId");
            related.Metadata.Remove("identitySuggestionDeviceId");
        }
        return true;
    }

    public void ReinheritDialogMetadata(CloudSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.DeviceId)) return;

        var link = Sync(_identityLinks.FindAsync(session.DeviceId));
        if (link is not null)
        {
            var linkedDevice = Sync(_devices.GetByDeviceIdAsync(link.InventoryDeviceId));
            if (linkedDevice is not null && !linkedDevice.IsHidden && linkedDevice.ArchivedUtc is null)
                ApplyRegisteredDeviceMetadata(session, linkedDevice);
        }

        var donor = _sessions.Values
            .Where(candidate => candidate.SessionId != session.SessionId &&
                                string.Equals(candidate.DeviceId, session.DeviceId,
                                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.LastSeenUtc)
            .FirstOrDefault();
        if (donor is null) return;
        foreach (var pair in donor.Metadata.Where(pair => ShouldInheritDialogMetadataKey(pair.Key)))
            if (!session.Metadata.ContainsKey(pair.Key)) session.Metadata[pair.Key] = pair.Value;
    }

    public void UpdateRobot(DeviceRegistration registration)
    {
        var robot = UpsertDevice(registration);
        if (_robotProfiles is not null)
            Sync(_robotProfiles.UpsertAsync(BuildDefaultRobotProfile(robot), robot.DeviceId));
    }

    private void RegisterIssuedToken(string token, string kind, string accountId, string? deviceId,
        TimeSpan lifetime)
    {
        var expiresUtc = DateTimeOffset.UtcNow.Add(lifetime);
        var metadata = BuildSessionMetadata(accountId, deviceId);
        var stored = Sync(_authTokens.IssueAsync(token, kind, accountId, deviceId, expiresUtc, metadata));
        _sessions.RegisterDurableToken(token,
            CreateSession(stored.TokenKind, stored.AccountId, stored.DeviceId, token, null, null, stored.IssuedUtc));
    }

    private static CloudSession CreateSession(string kind, string? accountId, string? deviceId, string token,
        string? hostName, string? path, DateTimeOffset? createdUtc = null) => new()
    {
        Kind = kind,
        AccountId = accountId,
        DeviceId = deviceId,
        Token = token,
        HostName = hostName,
        Path = path,
        CreatedUtc = createdUtc ?? DateTimeOffset.UtcNow,
        LastSeenUtc = DateTimeOffset.UtcNow,
        Metadata = BuildSessionMetadata(accountId, deviceId)
    };

    private static Dictionary<string, object?> BuildSessionMetadata(string? accountId, string? deviceId)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["loopId"] = DefaultLoopId
        };
        if (!string.IsNullOrWhiteSpace(accountId)) metadata["accountId"] = accountId;
        if (!string.IsNullOrWhiteSpace(deviceId)) metadata["deviceId"] = deviceId;
        return metadata;
    }

    private static void ApplyRegisteredDeviceMetadata(CloudSession session, DeviceRegistration device)
    {
        session.Metadata["registeredDeviceId"] = device.DeviceId;
        session.Metadata["registeredRobotId"] = device.RobotId;
    }

    private static bool ShouldInheritDialogMetadataKey(string key) =>
        key.Equals("registeredDeviceId", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("registeredRobotId", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("personalReport", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("householdList", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("chitchat", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("greetings", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("pendingProactivityOffer", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("lastClockDomain", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("sleepState", StringComparison.OrdinalIgnoreCase);

    private static bool IsAmbiguousConnectionToken(string token) =>
        token.StartsWith("conn:", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("v1/listen", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("listen", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("v1/proactive", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("proactive", StringComparison.OrdinalIgnoreCase);

    private static CloudSession CloneSession(CloudSession source)
    {
        var clone = CreateSession(source.Kind, source.AccountId, source.DeviceId,
            source.Token ?? $"conn:{Guid.NewGuid():N}", source.HostName, source.Path, source.CreatedUtc);
        clone.LastSeenUtc = source.LastSeenUtc;
        clone.FollowUpExpiresUtc = source.FollowUpExpiresUtc;
        clone.LastMessageType = source.LastMessageType;
        clone.LastListenType = source.LastListenType;
        clone.LastIntent = source.LastIntent;
        clone.LastTranscript = source.LastTranscript;
        clone.LastTransId = source.LastTransId;
        foreach (var pair in source.Metadata) clone.Metadata[pair.Key] = pair.Value;
        return clone;
    }

    private static DeviceRegistration CloneDevice(DeviceRegistration source, string? robotId = null,
        string? friendlyName = null, string? firmwareVersion = null, string? applicationVersion = null) => new()
    {
        DeviceId = source.DeviceId,
        RobotId = robotId ?? source.RobotId,
        FriendlyName = friendlyName ?? source.FriendlyName,
        FirmwareVersion = firmwareVersion ?? source.FirmwareVersion,
        ApplicationVersion = applicationVersion ?? source.ApplicationVersion,
        IsActive = source.IsActive,
        CertificateThumbprint = source.CertificateThumbprint,
        IssuedIdentityId = source.IssuedIdentityId,
        BuildHash = source.BuildHash,
        ConfigHash = source.ConfigHash,
        VerifiedSerialNumber = source.VerifiedSerialNumber,
        SerialEvidenceSource = source.SerialEvidenceSource,
        SerialEvidenceVerifiedUtc = source.SerialEvidenceVerifiedUtc,
        RegistrationSource = source.RegistrationSource,
        IsHidden = source.IsHidden,
        ArchivedUtc = source.ArchivedUtc,
        HostMappings = new Dictionary<string, string>(source.HostMappings, StringComparer.OrdinalIgnoreCase)
    };

    private static InvalidOperationException MissingDefault(string kind) => new(
        $"No default {kind} exists in normalized cloud state. Import or seed cloud state before starting.");

    private static T Sync<T>(Task<T> task) => task.ConfigureAwait(false).GetAwaiter().GetResult();
    private static void Sync(Task task) => task.ConfigureAwait(false).GetAwaiter().GetResult();

    private static TimeSpan PositiveLifetime(TimeSpan? configured, TimeSpan fallback) =>
        configured is { } value && value > TimeSpan.Zero ? value : fallback;
}
