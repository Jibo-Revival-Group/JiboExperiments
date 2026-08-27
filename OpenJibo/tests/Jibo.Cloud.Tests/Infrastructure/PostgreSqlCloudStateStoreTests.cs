using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed partial class PostgreSqlCloudStateStoreTests
{
    [Fact]
    public void RobotToken_IsDurableButDoesNotBecomeAnActiveSessionUntilOpened()
    {
        var tokens = new FakeTokenRepository();
        var store = CreateStore(tokens, robotTokenLifetime: TimeSpan.FromDays(45));

        var token = store.IssueRobotToken("device-1");

        Assert.Null(store.FindActiveSessionByToken(token));
        Assert.NotNull(store.FindSessionByToken(token));
        Assert.NotNull(tokens.Issued);
        Assert.True(tokens.Issued!.ExpiresUtc - tokens.Issued.IssuedUtc >= TimeSpan.FromDays(44));
    }

    [Fact]
    public void OpenSession_CopiesDurableMetadataIntoSeparateBoundedActiveSession()
    {
        var store = CreateStore(new FakeTokenRepository());
        var token = store.IssueRobotToken("device-1");
        var durable = Assert.IsType<CloudSession>(store.FindSessionByToken(token));
        durable.Metadata["persisted-marker"] = "yes";

        var active = store.OpenSession("hub", null, token, "neohub", "/v1/listen");

        Assert.NotSame(durable, active);
        Assert.Equal("yes", active.Metadata["persisted-marker"]);
        Assert.Same(active, store.FindActiveSessionByToken(token));
        store.CloseSession(active.SessionId);
        Assert.Null(store.FindActiveSessionByToken(token));
        Assert.NotNull(store.FindSessionByToken(token));
    }

    [Fact]
    public void UserMethods_DelegateToNormalizedRepository()
    {
        var users = new FakeUserRepository();
        var store = CreateStore(new FakeTokenRepository(), users: users);

        Assert.Same(users.User, store.CreateUser("ada@example.com", "password", "Ada", "Lovelace"));
        Assert.Same(users.User, store.AuthenticateUser("ada@example.com", "password"));
        Assert.Same(users.User, store.GetUserById("user-1"));
        Assert.Same(users.User, store.GetUserByEmail("ada@example.com"));
        Assert.Same(users.User, store.UpdateUser("user-1", "Augusta", null, "female", 18151210));
        Assert.Equal(5, users.CallCount);
    }

    [Fact]
    public void OperationalMethods_DelegateUsingDefaultAccountAndRequestedLoop()
    {
        var keys = new FakeLoopKeyRepository();
        var holidays = new FakeHolidayRepository();
        var store = CreateStore(new FakeTokenRepository(), loopKeys: keys, holidays: holidays);

        Assert.True(store.ShouldCreateSymmetricKey("loop-2"));
        var key = store.GetOrCreateSymmetricKey("loop-2");
        Assert.False(store.ShouldCreateSymmetricKey("loop-2"));
        Assert.Equal(key, store.GetOrCreateSymmetricKey("loop-2"));
        Assert.Equal("account-1", keys.AccountId);
        Assert.Equal("loop-2", keys.LoopId);

        var enabled = new HolidayRecord { Id = "enabled", LoopId = "loop-2", IsEnabled = true };
        holidays.Items = [enabled, new HolidayRecord { Id = "disabled", LoopId = "loop-2", IsEnabled = false }];
        Assert.Equal([enabled], store.GetHolidays("loop-2"));
        Assert.Equal(("account-1", "loop-2"), (holidays.AccountId, holidays.LoopId));
        Assert.Same(enabled, store.UpsertHoliday(enabled));
    }

    [Fact]
    public void TrustedServerMethods_IncludeInactiveServersAndPersistAdmissionsAndRevocations()
    {
        var trust = new FakeTrustedServerRepository();
        var store = CreateStore(new FakeTokenRepository(), trustedServers: trust);
        var server = new TrustedServerRecord
        {
            ServerId = "server-1",
            CanonicalHost = "cloud.example",
            IsActive = false
        };

        Assert.Same(server, store.UpsertTrustedServer(server));
        Assert.Same(server, store.FindTrustedServer("CLOUD.EXAMPLE."));
        Assert.True(trust.LastIncludeInactive);
        var admission = store.RecordTrustedServerAdmission(server, "admit", "device-1", "Kitchen Jibo", "test");
        Assert.Same(admission, Assert.Single(store.GetTrustedServerAdmissions("cloud.example")));
        Assert.NotEmpty(admission.Signature);

        store.RevokeIdentityGraphAnchor(" anchor-1 ");
        Assert.Contains("anchor-1", trust.RevokedAnchors);
    }

    [Fact]
    public void IdentityGraph_IsDeterministicAndScopedToDefaultRelationalIdentity()
    {
        var store = CreateStore(new FakeTokenRepository());

        var first = store.GetIdentityGraph("loop-2");
        var second = store.GetIdentityGraph("loop-2");

        Assert.Equal("account-1", first.AccountId);
        Assert.Equal("loop-2", first.LoopId);
        Assert.Equal("robot-1", first.RobotId);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(first.ContentHash, first.Signature);
        Assert.Contains(first.Relationships, item => item.SubjectId == "account-1" && item.ObjectId == "loop-2");
    }

    [Fact]
    public void GetPeople_WithLoopId_UsesOneScopedRepositoryQuery()
    {
        var people = new RecordingPersonRepository(new PersonRecord
        {
            PersonId = "person-1", AccountId = "account-1", LoopId = "loop-2", RobotId = "robot-1"
        });
        var store = CreateStore(new FakeTokenRepository(), people: people);

        var result = store.GetPeople("  loop-2  ");

        Assert.Single(result);
        Assert.Equal(1, people.ListCallCount);
        Assert.Equal(("account-1", "loop-2", 1000),
            (people.LastAccountId, people.LastLoopId, people.LastLimit));
    }

    [Fact]
    public void InMemoryGetPeople_WithLoopId_PreservesCaseInsensitiveCompatibility()
    {
        var store = new InMemoryCloudStateStore();
        var accountId = store.GetAccount().AccountId;
        var robotId = store.GetRobot().RobotId;
        store.UpsertPerson(new PersonRecord
            { PersonId = "scoped-a", AccountId = accountId, LoopId = "loop-a", RobotId = robotId });
        store.UpsertPerson(new PersonRecord
            { PersonId = "scoped-b", AccountId = accountId, LoopId = "loop-b", RobotId = robotId });

        var result = store.GetPeople("LOOP-A");

        Assert.Contains(result, person => person.PersonId == "scoped-a");
        Assert.DoesNotContain(result, person => person.PersonId == "scoped-b");
    }

    [Fact]
    public void SyncPeopleFromLoopUsers_UsesResolvedLoopOwnerInsteadOfDefaultAccount()
    {
        var people = new RecordingPersonRepository();
        var members = new RecordingLoopMemberRepository();
        var store = CreateStore(new FakeTokenRepository(), people: people, members: members);

        var count = store.SyncPeopleFromLoopUsers(
            "tenant-loop",
            "tenant-robot",
            [new LoopUserSnapshot("person-1", FirstName: "Ada", Type: "owner")],
            "tenant-account");

        Assert.Equal(1, count);
        Assert.Equal("tenant-account", people.LastUpsert!.AccountId);
        Assert.Equal("tenant-loop", people.LastUpsert.LoopId);
        Assert.Equal("tenant-account", members.LastAccountId);
        Assert.Equal("tenant-loop", members.LastUpsert!.LoopId);
    }

    private static PostgreSqlCloudStateStore CreateStore(FakeTokenRepository tokens,
        TimeSpan? robotTokenLifetime = null, ICloudUserRepository? users = null,
        ILoopKeyRepository? loopKeys = null, IHolidayOverrideRepository? holidays = null,
        ITrustedServerRepository? trustedServers = null, IUpdateManifestRepository? updates = null,
        IMediaMetadataRepository? media = null, IBackupManifestRepository? backups = null,
        IBackupPayloadStore? backupPayloads = null, ILoopTopologyRepository? loops = null,
        ILoopMemberRepository? members = null, IPersonRepository? people = null,
        IRecognitionObservationRepository? recognition = null,
        ICommuteProfileRepository? commutes = null, ICalendarEventRepository? calendar = null,
        IGreetingPresenceRepository? greetings = null, IAtomicLoopBackupRestorer? atomicBackupRestorer = null,
        ICloudStateSecretProtector? secretProtector = null)
    {
        var account = new AccountProfile { AccountId = "account-1", Email = "owner@example.com" };
        var device = new DeviceRegistration
        {
            DeviceId = "device-1",
            RobotId = "robot-1",
            FriendlyName = "Kitchen Jibo",
            RegistrationSource = RobotRegistrationSources.Physical
        };
        return new PostgreSqlCloudStateStore(
            new FakeMetadataRepository(),
            new FakeAccountRepository(account),
            new FakeDeviceRepository(device),
            tokens,
            new FakeIdentityLinkRepository(),
            new BoundedCloudSessionRegistry(4, 4),
            robotTokenLifetime: robotTokenLifetime,
            users: users ?? new FakeUserRepository(),
            loopKeys: loopKeys,
            holidays: holidays,
            trustedServers: trustedServers,
            updates: updates,
            media: media,
            backups: backups,
            backupPayloads: backupPayloads,
            atomicBackupRestorer: atomicBackupRestorer,
            secretProtector: secretProtector,
            loops: loops,
            members: members,
            people: people,
            recognition: recognition,
            commutes: commutes,
            calendar: calendar,
            greetings: greetings);
    }

    private sealed class FakeMetadataRepository : ICloudStateMetadataRepository
    {
        public Task<CloudStateMetadataRecord> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CloudStateMetadataRecord(2, 0, DateTimeOffset.UtcNow));
    }

    private sealed class RecordingPersonRepository(params PersonRecord[] records) : IPersonRepository
    {
        internal int ListCallCount { get; private set; }
        internal string? LastAccountId { get; private set; }
        internal string? LastLoopId { get; private set; }
        internal int LastLimit { get; private set; }
        internal PersonRecord? LastUpsert { get; private set; }

        public Task<IReadOnlyList<PersonRecord>> ListAsync(string accountId, string loopId, int limit = 250,
            CancellationToken cancellationToken = default)
        {
            ListCallCount++;
            LastAccountId = accountId;
            LastLoopId = loopId;
            LastLimit = limit;
            return Task.FromResult<IReadOnlyList<PersonRecord>>(records);
        }

        public Task<PersonRecord> UpsertAsync(PersonRecord person,
            CancellationToken cancellationToken = default)
        {
            LastUpsert = person;
            return Task.FromResult(person);
        }

        public Task<bool> DeleteAsync(string accountId, string loopId, string personId,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class RecordingLoopMemberRepository : ILoopMemberRepository
    {
        internal string? LastAccountId { get; private set; }
        internal LoopMemberRecord? LastUpsert { get; private set; }

        public Task<IReadOnlyList<LoopMemberRecord>> ListAsync(string accountId, string loopId, int limit = 250,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LoopMemberRecord>>([]);

        public Task<LoopMemberRecord?> GetAsync(string accountId, string loopId, string memberId,
            CancellationToken cancellationToken = default) => Task.FromResult<LoopMemberRecord?>(null);

        public Task<LoopMemberRecord> UpsertAsync(string accountId, LoopMemberRecord member,
            CancellationToken cancellationToken = default)
        {
            LastAccountId = accountId;
            LastUpsert = member;
            return Task.FromResult(member);
        }

        public Task<bool> DeleteAsync(string accountId, string loopId, string memberId,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FakeAccountRepository(AccountProfile account) : ICloudAccountRepository
    {
        public Task<AccountProfile?> GetByIdAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountProfile?>(accountId == account.AccountId ? account : null);
        public Task<AccountProfile?> GetDefaultAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountProfile?>(account);
        public Task<AccountProfile> UpsertAsync(AccountProfile value, bool? isDefault = null,
            CancellationToken cancellationToken = default) => Task.FromResult(value);
    }

    private sealed class FakeDeviceRepository(DeviceRegistration device) : ICloudDeviceRepository
    {
        public Task<DeviceRegistration?> GetByDeviceIdAsync(string deviceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DeviceRegistration?>(deviceId == device.DeviceId ? device : null);
        public Task<DeviceRegistration?> FindByFriendlyIdAsync(string friendlyId,
            CancellationToken cancellationToken = default) => Task.FromResult<DeviceRegistration?>(device);
        public Task<DeviceRegistration?> GetDefaultAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DeviceRegistration?>(device);
        public Task<IReadOnlyList<DeviceRegistration>> ListForAccountAsync(string accountId,
            bool includeArchived = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceRegistration>>([device]);
        public Task<IReadOnlyList<DeviceRegistration>> ListAllAsync(bool includeArchived = true,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceRegistration>>([device]);
        public Task<IReadOnlyList<string>> ListAccountIdsAsync(string deviceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["test-account"]);
        public Task<DeviceRegistration> UpsertAsync(DeviceRegistration value, string? accountId = null,
            bool? isDefault = null, CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task<RobotCredentialBinding?> GetCredentialBindingAsync(string accessKeyFingerprint,
            CancellationToken cancellationToken = default) => Task.FromResult<RobotCredentialBinding?>(null);
        public Task<IReadOnlyList<RobotCredentialBinding>> ListCredentialBindingsAsync(string deviceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RobotCredentialBinding>>([]);
        public Task<IReadOnlyList<RobotCredentialBinding>> ListCredentialBindingsForAccountAsync(string accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RobotCredentialBinding>>([]);
        public Task<RobotCredentialBinding> BindCredentialAsync(string deviceId, string accessKeyFingerprint,
            string claimSource, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RobotCredentialBinding>> SwapCredentialBindingsAsync(
            string firstAccessKeyFingerprint, string secondAccessKeyFingerprint, string claimSource,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeviceRegistration?> FindByCredentialFingerprintAsync(string accessKeyFingerprint,
            CancellationToken cancellationToken = default) => Task.FromResult<DeviceRegistration?>(null);
        public Task<int> MoveCredentialBindingsAsync(string sourceDeviceId, string targetDeviceId,
            string claimSource, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeTokenRepository : ICloudAuthTokenRepository
    {
        internal CloudAuthTokenRecord? Issued { get; private set; }
        public Task<CloudAuthTokenRecord> IssueAsync(string token, string tokenKind, string? accountId,
            string? deviceId, DateTimeOffset expiresUtc, IReadOnlyDictionary<string, object?>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            Issued = new CloudAuthTokenRecord(tokenKind, accountId, deviceId, DateTimeOffset.UtcNow, expiresUtc,
                null, metadata ?? new Dictionary<string, object?>());
            return Task.FromResult(Issued);
        }
        public Task<CloudAuthTokenRecord?> FindValidAsync(string token, DateTimeOffset? now = null,
            CancellationToken cancellationToken = default) => Task.FromResult(Issued);
        public Task<bool> RevokeAsync(string token, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<int> RevokeForAccountAsync(string accountId,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeIdentityLinkRepository : IRobotIdentityLinkRepository
    {
        public Task<IReadOnlyList<RobotIdentityLinkRecord>> ListForAccountAsync(string accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RobotIdentityLinkRecord>>([]);
        public Task<RobotIdentityLinkRecord?> FindAsync(string observedDeviceId,
            CancellationToken cancellationToken = default) => Task.FromResult<RobotIdentityLinkRecord?>(null);
        public Task<RobotIdentityLinkRecord> UpsertAsync(string observedDeviceId, string inventoryDeviceId,
            string claimSource, IReadOnlyList<RobotIdentityLinkAuditEntry>? audit = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RevokeAsync(string observedDeviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeUserRepository : ICloudUserRepository
    {
        internal UserRecord User { get; } = new() { Id = "user-1", Email = "ada@example.com" };
        internal int CallCount { get; private set; }

        public Task<UserRecord?> CreateAsync(string email, string password, string? firstName, string? lastName,
            CancellationToken cancellationToken = default) => Return();
        public Task<UserRecord?> AuthenticateAsync(string email, string password,
            CancellationToken cancellationToken = default) => Return();
        public Task<UserRecord?> GetByIdAsync(string userId, CancellationToken cancellationToken = default) =>
            Return();
        public Task<UserRecord?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Return();
        public Task<UserRecord> UpdateProfileAsync(string userId, string? firstName, string? lastName,
            string? gender, long? birthday, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(User);
        }

        private Task<UserRecord?> Return()
        {
            CallCount++;
            return Task.FromResult<UserRecord?>(User);
        }
    }

    private sealed class FakeLoopKeyRepository : ILoopKeyRepository
    {
        private LoopSymmetricKeyRecord? _key;
        internal string? AccountId { get; private set; }
        internal string? LoopId { get; private set; }

        public Task<LoopSymmetricKeyRecord?> GetAsync(string accountId, string loopId,
            CancellationToken cancellationToken = default)
        {
            AccountId = accountId;
            LoopId = loopId;
            return Task.FromResult(_key?.LoopId == loopId ? _key : null);
        }

        public Task<LoopSymmetricKeyRecord> UpsertAsync(string accountId, LoopSymmetricKeyRecord key,
            CancellationToken cancellationToken = default)
        {
            AccountId = accountId;
            LoopId = key.LoopId;
            _key = key;
            return Task.FromResult(key);
        }

        public Task<IReadOnlyList<StoredKeyRequest>> ListRequestsAsync(string accountId, string loopId,
            int limit = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredKeyRequest>>([]);

        public Task<StoredKeyRequest> UpsertRequestAsync(string accountId, StoredKeyRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(request);

        public Task<bool> DeleteRequestAsync(string accountId, string loopId, string requestId,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FakeHolidayRepository : IHolidayOverrideRepository
    {
        internal IReadOnlyList<HolidayRecord> Items { get; set; } = [];
        internal string? AccountId { get; private set; }
        internal string? LoopId { get; private set; }

        public Task<IReadOnlyList<HolidayRecord>> ListAsync(string accountId, string loopId, int limit = 250,
            CancellationToken cancellationToken = default)
        {
            AccountId = accountId;
            LoopId = loopId;
            return Task.FromResult(Items);
        }

        public Task<HolidayRecord> UpsertAsync(string accountId, HolidayRecord holiday,
            CancellationToken cancellationToken = default) => Task.FromResult(holiday);

        public Task<bool> DeleteAsync(string accountId, string loopId, string holidayId,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FakeTrustedServerRepository : ITrustedServerRepository
    {
        private readonly List<TrustedServerRecord> _servers = [];
        private readonly List<TrustedServerAdmissionRecord> _admissions = [];
        internal bool LastIncludeInactive { get; private set; }
        internal HashSet<string> RevokedAnchors { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<TrustedServerRecord>> ListAsync(bool includeInactive = false, int limit = 250,
            CancellationToken cancellationToken = default)
        {
            LastIncludeInactive = includeInactive;
            return Task.FromResult<IReadOnlyList<TrustedServerRecord>>(_servers
                .Where(item => includeInactive || item.IsActive).Take(limit).ToArray());
        }

        public Task<TrustedServerRecord> UpsertAsync(TrustedServerRecord server,
            CancellationToken cancellationToken = default)
        {
            _servers.RemoveAll(item => item.ServerId == server.ServerId);
            _servers.Add(server);
            return Task.FromResult(server);
        }

        public Task<IReadOnlyList<TrustedServerAdmissionRecord>> ListAdmissionsAsync(string serverId,
            int limit = 250, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TrustedServerAdmissionRecord>>(_admissions
                .Where(item => item.ServerId == serverId).Take(limit).ToArray());

        public Task<TrustedServerAdmissionRecord> AddAdmissionAsync(TrustedServerAdmissionRecord admission,
            CancellationToken cancellationToken = default)
        {
            _admissions.Add(admission);
            return Task.FromResult(admission);
        }

        public Task<bool> RevokeAnchorAsync(string anchor, string? reason,
            CancellationToken cancellationToken = default) => Task.FromResult(RevokedAnchors.Add(anchor));

        public Task<bool> IsAnchorRevokedAsync(string anchor, CancellationToken cancellationToken = default) =>
            Task.FromResult(RevokedAnchors.Contains(anchor));
    }
}
