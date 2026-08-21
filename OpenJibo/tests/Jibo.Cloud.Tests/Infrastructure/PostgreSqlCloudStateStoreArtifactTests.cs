using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed partial class PostgreSqlCloudStateStoreTests
{
    [Fact]
    public void ListUpdates_AllSubsystemQueriesAllAndPreservesFilter()
    {
        var updates = new ArtifactUpdateRepository
        {
            Items =
            [
                StoredUpdate("robot", "1.1.0", "stable"),
                StoredUpdate("voice", "2.0.0", "stable")
            ]
        };
        var store = CreateStore(new FakeTokenRepository(), updates: updates);

        var result = store.ListUpdates(" ALL ", "stable");

        Assert.Equal(2, result.Count);
        Assert.Null(updates.LastSubsystem);
        Assert.Equal("stable", updates.LastFilter);
        Assert.Equal("1.1.0", store.GetUpdateFrom("*", "1.0.0", "stable")!.ToVersion);
    }

    [Fact]
    public void MediaMethodsAlwaysPassDefaultAccountScope()
    {
        var media = new ArtifactMediaRepository();
        var store = CreateStore(new FakeTokenRepository(), media: media);
        var after = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var created = store.CreateMedia("loop-1", "/photo/one", "image", "camera", false, null);
        _ = store.ListMedia(["loop-1"], after, before);
        _ = store.GetMedia([created.Path]);
        _ = store.RemoveMedia([created.Path]);

        Assert.All(media.AccountScopes, value => Assert.Equal("account-1", value));
        Assert.Equal(["loop-1"], media.LastLoopIds);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(after), media.LastAfter);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(before), media.LastBefore);
        Assert.Equal("account-1", created.AccountId);
    }

    [Fact]
    public void BackupExternalizesLoopPayloadAndRestoresEveryScopedFamily()
    {
        var loops = new ArtifactLoopRepository(new LoopRecord
        {
            LoopId = "loop-1", OwnerAccountId = "account-1", RobotId = "robot-1",
            RobotFriendlyId = "Kitchen Jibo"
        });
        var members = new ArtifactMemberRepository(new LoopMemberRecord { Id = "member-1", LoopId = "loop-1" });
        var people = new ArtifactPersonRepository(new PersonRecord
        {
            PersonId = "person-1", AccountId = "account-1", LoopId = "loop-1", RobotId = "robot-1"
        });
        var holidays = new ArtifactHolidayRepository(new HolidayRecord { Id = "holiday-1", LoopId = "loop-1" });
        var commutes = new ArtifactCommuteRepository(new CommuteProfileRecord { Id = "commute-1", LoopId = "loop-1" });
        var calendar = new ArtifactCalendarRepository(new CalendarEventRecord { Id = "event-1", LoopId = "loop-1" });
        var greetings = new ArtifactGreetingRepository(new GreetingPresenceRecord
        {
            Id = "greeting-1", AccountId = "account-1", LoopId = "loop-1", PersonId = "person-1"
        });
        var manifests = new ArtifactBackupRepository();
        var payloads = new ArtifactPayloadStore();
        var recognition = new ArtifactRecognitionRepository(new RecognitionObservationRecord
        {
            ObservationId = "observation-1", LoopId = "loop-1", MemberId = "member-1", RobotId = "robot-1"
        });
        var keys = new ArtifactBackupLoopKeyRepository(
            new LoopSymmetricKeyRecord("loop-1", [1, 2], "wrap-1", "AES-256-GCM", DateTimeOffset.UtcNow),
            new StoredKeyRequest(new KeyRequestRecord { RequestId = "request-1", LoopId = "loop-1" }));
        var media = new ArtifactMediaRepository();
        media.Seed(new StoredMediaRecord(new MediaRecord
        {
            Path = "/media/one", AccountId = "account-1", LoopId = "loop-1", Url = "blob:///one"
        }));
        var store = CreateStore(new FakeTokenRepository(), loops: loops, members: members, people: people,
            holidays: holidays, commutes: commutes, calendar: calendar, greetings: greetings,
            recognition: recognition, loopKeys: keys, media: media, backups: manifests, backupPayloads: payloads);

        var backup = store.CreateBackup("LOOP-1", " Before changes ");

        Assert.Null(backup.SnapshotJson);
        Assert.Equal("loop-1", backup.LoopId);
        Assert.Equal("Before changes", backup.Name);
        Assert.Contains("cloud-backups/account-1/", payloads.LastKey, StringComparison.Ordinal);
        Assert.NotNull(manifests.Item);
        Assert.Equal(payloads.LastSha256, manifests.Item!.ContentSha256);
        Assert.Equal(payloads.Payload!.LongLength, manifests.Item.ContentLength);
        Assert.Equal(2, manifests.Item.BackupSchemaVersion);
        var externalized = JsonSerializer.Deserialize<RelationalLoopBackup>(payloads.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Single(externalized!.RecognitionObservations);
        Assert.NotNull(externalized.LoopKey);
        Assert.Single(externalized.KeyRequests);
        Assert.Single(externalized.Media);

        loops.Clear(); members.Clear(); people.Clear(); holidays.Clear(); commutes.Clear(); calendar.Clear(); greetings.Clear();
        var restored = store.RestoreBackup(backup.BackupId);

        Assert.Equal(backup.BackupId, restored!.BackupId);
        Assert.Single(loops.Items);
        Assert.Single(members.Items);
        Assert.Single(people.Items);
        Assert.Single(holidays.Items);
        Assert.Single(commutes.Items);
        Assert.Single(calendar.Items);
        Assert.Single(greetings.Items);
        Assert.True(manifests.MarkedRestored);
    }

    [Fact]
    public void RestoreBackupRejectsCorruptPayloadBeforeWritingState()
    {
        var loops = new ArtifactLoopRepository(new LoopRecord
            { LoopId = "loop-1", OwnerAccountId = "account-1", RobotId = "robot-1" });
        var manifests = new ArtifactBackupRepository();
        var payloads = new ArtifactPayloadStore();
        var store = CreateStore(new FakeTokenRepository(), loops: loops,
            members: new ArtifactMemberRepository(), people: new ArtifactPersonRepository(),
            holidays: new ArtifactHolidayRepository(), commutes: new ArtifactCommuteRepository(),
            calendar: new ArtifactCalendarRepository(), greetings: new ArtifactGreetingRepository(),
            backups: manifests, backupPayloads: payloads);
        var backup = store.CreateBackup("loop-1", "test");
        var writesBeforeRestore = loops.UpsertCount;
        payloads.Payload![0] ^= 0xff;

        Assert.Throws<InvalidDataException>(() => store.RestoreBackup(backup.BackupId));
        Assert.Equal(writesBeforeRestore, loops.UpsertCount);
        Assert.False(manifests.MarkedRestored);
    }

    [Fact]
    public void RestoreBackupUsesAtomicCoordinatorWhenConfigured()
    {
        var loops = new ArtifactLoopRepository(new LoopRecord
            { LoopId = "loop-1", OwnerAccountId = "account-1", RobotId = "robot-1" });
        var manifests = new ArtifactBackupRepository();
        var payloads = new ArtifactPayloadStore();
        var atomic = new ArtifactAtomicBackupRestorer();
        var store = CreateStore(new FakeTokenRepository(), loops: loops,
            members: new ArtifactMemberRepository(), people: new ArtifactPersonRepository(),
            holidays: new ArtifactHolidayRepository(), commutes: new ArtifactCommuteRepository(),
            calendar: new ArtifactCalendarRepository(), greetings: new ArtifactGreetingRepository(),
            backups: manifests, backupPayloads: payloads, atomicBackupRestorer: atomic);
        var backup = store.CreateBackup("loop-1", "test");

        _ = store.RestoreBackup(backup.BackupId);

        Assert.Equal(1, atomic.CallCount);
        Assert.Equal("account-1", atomic.AccountId);
        Assert.Equal(backup.BackupId, atomic.Manifest?.BackupId);
        Assert.False(manifests.MarkedRestored);
    }

    [Fact]
    public void RestoreBackupAdaptsImportedLegacySnapshotWithoutMutatingSourcePayload()
    {
        var sourceBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Account = new AccountProfile { AccountId = "account-1", Email = "owner@example.com" },
            Loops = new[] { new LoopRecord { LoopId = "loop-1", OwnerAccountId = "account-1", RobotId = "robot-1" } },
            LoopMembers = new[] { new LoopMemberRecord { Id = "legacy-member", LoopId = "loop-1" } },
            RecognitionObservations = new[] { new RecognitionObservationRecord
            {
                ObservationId = "legacy-observation", LoopId = "loop-1", MemberId = "legacy-member", RobotId = "robot-1"
            } },
            SymmetricKeys = new Dictionary<string, string> { ["loop-1"] = "legacy-plaintext-key" },
            KeyRequests = new[] { new KeyRequestRecord { RequestId = "legacy-request", LoopId = "loop-1" } },
            Media = new[] { new MediaRecord
            {
                Path = "/legacy/media", AccountId = "account-1", LoopId = "loop-1", Url = "blob:///legacy"
            } }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var originalBytes = sourceBytes.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        var manifests = new ArtifactBackupRepository();
        manifests.Seed(new BackupManifestRecord("legacy-backup", "account-1", "loop-1", "legacy",
            "memory:///legacy", sha, sourceBytes.LongLength, 1, "available", DateTimeOffset.UtcNow));
        var payloads = new ArtifactPayloadStore();
        payloads.Seed(sourceBytes);
        var atomic = new ArtifactAtomicBackupRestorer();
        var protector = new ArtifactSecretProtector();
        var store = CreateStore(new FakeTokenRepository(), backups: manifests, backupPayloads: payloads,
            atomicBackupRestorer: atomic, secretProtector: protector);

        _ = store.RestoreBackup("legacy-backup");

        Assert.Equal(originalBytes, payloads.Payload);
        Assert.Equal("legacy-member", Assert.Single(atomic.Backup!.Members).Id);
        Assert.Single(atomic.Backup.RecognitionObservations);
        Assert.Single(atomic.Backup.KeyRequests);
        Assert.Single(atomic.Backup.Media);
        Assert.Equal("protected:legacy-plaintext-key", System.Text.Encoding.UTF8.GetString(
            atomic.Backup.LoopKey!.EncryptedKey));
        Assert.Equal(1, protector.ProtectCount);
    }

    private static StoredUpdateManifest StoredUpdate(string subsystem, string version, string filter) => new(
        new UpdateManifest { UpdateId = $"{subsystem}-{version}", Subsystem = subsystem, ToVersion = version, Filter = filter },
        new Dictionary<string, object?>());

    private sealed class ArtifactUpdateRepository : IUpdateManifestRepository
    {
        internal IReadOnlyList<StoredUpdateManifest> Items { get; init; } = [];
        internal string? LastSubsystem { get; private set; }
        internal string? LastFilter { get; private set; }
        public Task<IReadOnlyList<StoredUpdateManifest>> ListAsync(string? subsystem = null, string? filter = null,
            CancellationToken cancellationToken = default)
        {
            LastSubsystem = subsystem; LastFilter = filter;
            return Task.FromResult<IReadOnlyList<StoredUpdateManifest>>(Items
                .Where(x => subsystem is null || x.Manifest.Subsystem.Equals(subsystem, StringComparison.OrdinalIgnoreCase))
                .Where(x => filter is null || string.Equals(x.Manifest.Filter, filter, StringComparison.OrdinalIgnoreCase))
                .ToArray());
        }
        public Task<StoredUpdateManifest?> GetAsync(string updateId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(x => x.Manifest.UpdateId == updateId));
        public Task<StoredUpdateManifest> UpsertAsync(StoredUpdateManifest update,
            CancellationToken cancellationToken = default) => Task.FromResult(update);
        public Task<StoredUpdateManifest?> DeleteAsync(string updateId,
            CancellationToken cancellationToken = default) => GetAsync(updateId, cancellationToken);
    }

    private sealed class ArtifactMediaRepository : IMediaMetadataRepository
    {
        private readonly Dictionary<string, StoredMediaRecord> _items = new(StringComparer.OrdinalIgnoreCase);
        internal List<string> AccountScopes { get; } = [];
        internal IReadOnlyList<string>? LastLoopIds { get; private set; }
        internal DateTimeOffset? LastAfter { get; private set; }
        internal DateTimeOffset? LastBefore { get; private set; }
        internal void Seed(StoredMediaRecord media) => _items[media.Media.Path] = media;
        public Task<IReadOnlyList<StoredMediaRecord>> ListAsync(string accountId, IReadOnlyList<string>? loopIds = null,
            DateTimeOffset? after = null, DateTimeOffset? before = null, int limit = 250,
            CancellationToken cancellationToken = default)
        {
            AccountScopes.Add(accountId); LastLoopIds = loopIds; LastAfter = after; LastBefore = before;
            return Task.FromResult<IReadOnlyList<StoredMediaRecord>>(_items.Values.ToArray());
        }
        public Task<IReadOnlyList<StoredMediaRecord>> GetAsync(string accountId, IReadOnlyList<string> paths,
            CancellationToken cancellationToken = default)
        {
            AccountScopes.Add(accountId);
            return Task.FromResult<IReadOnlyList<StoredMediaRecord>>(paths.Where(_items.ContainsKey).Select(x => _items[x]).ToArray());
        }
        public Task<StoredMediaRecord> UpsertAsync(StoredMediaRecord media, CancellationToken cancellationToken = default)
        {
            AccountScopes.Add(media.Media.AccountId); _items[media.Media.Path] = media; return Task.FromResult(media);
        }
        public Task<IReadOnlyList<StoredMediaRecord>> SoftDeleteAsync(string accountId, IReadOnlyList<string> paths,
            CancellationToken cancellationToken = default)
        {
            AccountScopes.Add(accountId);
            return Task.FromResult<IReadOnlyList<StoredMediaRecord>>(paths.Where(_items.ContainsKey).Select(x => _items[x]).ToArray());
        }
    }

    private sealed class ArtifactRecognitionRepository(params RecognitionObservationRecord[] values)
        : IRecognitionObservationRepository
    {
        private readonly List<RecognitionObservationRecord> _items = [.. values];
        public Task<IReadOnlyList<RecognitionObservationRecord>> ListAsync(string accountId, string loopId,
            int limit = 250, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecognitionObservationRecord>>(
                _items.Where(item => item.LoopId == loopId).Take(limit).ToArray());
        public Task<RecognitionObservationRecord> AddAsync(string accountId,
            RecognitionObservationRecord observation, CancellationToken cancellationToken = default)
        { _items.Add(observation); return Task.FromResult(observation); }
    }

    private sealed class ArtifactBackupLoopKeyRepository(LoopSymmetricKeyRecord key, params StoredKeyRequest[] requests)
        : ILoopKeyRepository
    {
        public Task<LoopSymmetricKeyRecord?> GetAsync(string accountId, string loopId,
            CancellationToken cancellationToken = default) => Task.FromResult<LoopSymmetricKeyRecord?>(key);
        public Task<LoopSymmetricKeyRecord> UpsertAsync(string accountId, LoopSymmetricKeyRecord value,
            CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task<IReadOnlyList<StoredKeyRequest>> ListRequestsAsync(string accountId, string loopId,
            int limit = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredKeyRequest>>(requests.Take(limit).ToArray());
        public Task<StoredKeyRequest> UpsertRequestAsync(string accountId, StoredKeyRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(request);
        public Task<bool> DeleteRequestAsync(string accountId, string loopId, string requestId,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class ArtifactBackupRepository : IBackupManifestRepository
    {
        internal BackupManifestRecord? Item { get; private set; }
        internal bool MarkedRestored { get; private set; }
        internal void Seed(BackupManifestRecord item) => Item = item;
        public Task<IReadOnlyList<BackupManifestRecord>> ListAsync(string accountId, string? loopId = null,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BackupManifestRecord>>(Item is null ? [] : [Item]);
        public Task<BackupManifestRecord?> GetAsync(string accountId, string backupId,
            CancellationToken cancellationToken = default) => Task.FromResult(Item?.BackupId == backupId ? Item : null);
        public Task<BackupManifestRecord> UpsertAsync(BackupManifestRecord backup,
            CancellationToken cancellationToken = default) { Item = backup; return Task.FromResult(backup); }
        public Task<BackupManifestRecord?> MarkRestoredAsync(string accountId, string backupId, DateTimeOffset restoredUtc,
            CancellationToken cancellationToken = default) { MarkedRestored = true; return Task.FromResult(Item); }
    }

    private sealed class ArtifactPayloadStore : IBackupPayloadStore
    {
        internal string LastKey { get; private set; } = string.Empty;
        internal string LastSha256 { get; private set; } = string.Empty;
        internal byte[]? Payload { get; set; }
        internal void Seed(byte[] payload) => Payload = payload.ToArray();
        public Task<string> StoreAsync(string key, byte[] payload, string sha256,
            CancellationToken cancellationToken = default)
        { LastKey = key; LastSha256 = sha256; Payload = payload.ToArray(); return Task.FromResult($"memory:///{key}"); }
        public Task<byte[]?> LoadAsync(string uri, CancellationToken cancellationToken = default) => Task.FromResult(Payload);
    }

    private sealed class ArtifactAtomicBackupRestorer : IAtomicLoopBackupRestorer
    {
        internal int CallCount { get; private set; }
        internal string? AccountId { get; private set; }
        internal BackupManifestRecord? Manifest { get; private set; }
        internal RelationalLoopBackup? Backup { get; private set; }
        public Task RestoreAsync(string accountId, BackupManifestRecord manifest, RelationalLoopBackup backup,
            DateTimeOffset restoredUtc, CancellationToken cancellationToken = default)
        {
            CallCount++; AccountId = accountId; Manifest = manifest; Backup = backup; return Task.CompletedTask;
        }
    }

    private sealed class ArtifactSecretProtector : ICloudStateSecretProtector
    {
        public string KeyId => "test-key";
        internal int ProtectCount { get; private set; }
        public byte[] Protect(string plaintext)
        {
            ProtectCount++;
            return System.Text.Encoding.UTF8.GetBytes($"protected:{plaintext}");
        }
        public string Unprotect(byte[] ciphertext) => throw new NotSupportedException();
    }

    private sealed class ArtifactLoopRepository(params LoopRecord[] values) : ILoopTopologyRepository
    {
        internal List<StoredLoopTopology> Items { get; } = values.Select(x => new StoredLoopTopology(x, [])).ToList();
        internal int UpsertCount { get; private set; }
        internal void Clear() => Items.Clear();
        public Task<IReadOnlyList<StoredLoopTopology>> ListForAccountAsync(string accountId, int limit = 100,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoredLoopTopology>>(Items.Take(limit).ToArray());
        public Task<IReadOnlyList<StoredLoopTopology>> ListForDeviceAsync(string accountId, string deviceId, int limit = 100,
            CancellationToken cancellationToken = default) => ListForAccountAsync(accountId, limit, cancellationToken);
        public Task<StoredLoopTopology?> GetAsync(string accountId, string loopId,
            CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(x => x.Loop.LoopId == loopId));
        public Task<StoredLoopTopology> UpsertAsync(StoredLoopTopology topology,
            CancellationToken cancellationToken = default)
        { UpsertCount++; Items.RemoveAll(x => x.Loop.LoopId == topology.Loop.LoopId); Items.Add(topology); return Task.FromResult(topology); }
        public Task<bool> DeleteAsync(string accountId, string loopId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.RemoveAll(x => x.Loop.LoopId == loopId) > 0);
    }

    private sealed class ArtifactMemberRepository(params LoopMemberRecord[] values) : ILoopMemberRepository
    {
        internal List<LoopMemberRecord> Items { get; } = [.. values];
        internal void Clear() => Items.Clear();
        public Task<IReadOnlyList<LoopMemberRecord>> ListAsync(string accountId, string loopId, int limit = 250,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LoopMemberRecord>>(Items.Where(x => x.LoopId == loopId).ToArray());
        public Task<LoopMemberRecord?> GetAsync(string accountId, string loopId, string memberId,
            CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(x => x.Id == memberId));
        public Task<LoopMemberRecord> UpsertAsync(string accountId, LoopMemberRecord member,
            CancellationToken cancellationToken = default) { Items.RemoveAll(x => x.Id == member.Id); Items.Add(member); return Task.FromResult(member); }
        public Task<bool> DeleteAsync(string accountId, string loopId, string memberId,
            CancellationToken cancellationToken = default) => Task.FromResult(Items.RemoveAll(x => x.Id == memberId) > 0);
    }

    private sealed class ArtifactPersonRepository(params PersonRecord[] values) : IPersonRepository
    {
        internal List<PersonRecord> Items { get; } = [.. values];
        internal void Clear() => Items.Clear();
        public Task<IReadOnlyList<PersonRecord>> ListAsync(string accountId, string loopId, int limit = 250,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PersonRecord>>(Items.Where(x => x.AccountId == accountId && x.LoopId == loopId).ToArray());
        public Task<PersonRecord> UpsertAsync(PersonRecord person, CancellationToken cancellationToken = default)
        { Items.RemoveAll(x => x.PersonId == person.PersonId); Items.Add(person); return Task.FromResult(person); }
        public Task<bool> DeleteAsync(string accountId, string loopId, string personId,
            CancellationToken cancellationToken = default) => Task.FromResult(Items.RemoveAll(x => x.PersonId == personId) > 0);
    }

    private sealed class ArtifactHolidayRepository(params HolidayRecord[] values) : IHolidayOverrideRepository
    {
        internal List<HolidayRecord> Items { get; } = [.. values]; internal void Clear() => Items.Clear();
        public Task<IReadOnlyList<HolidayRecord>> ListAsync(string accountId, string loopId, int limit = 250, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HolidayRecord>>(Items.Where(x => x.LoopId == loopId).ToArray());
        public Task<HolidayRecord> UpsertAsync(string accountId, HolidayRecord value, CancellationToken cancellationToken = default) { Items.RemoveAll(x => x.Id == value.Id); Items.Add(value); return Task.FromResult(value); }
        public Task<bool> DeleteAsync(string accountId, string loopId, string holidayId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
    private sealed class ArtifactCommuteRepository(params CommuteProfileRecord[] values) : ICommuteProfileRepository
    {
        internal List<CommuteProfileRecord> Items { get; } = [.. values]; internal void Clear() => Items.Clear();
        public Task<IReadOnlyList<CommuteProfileRecord>> ListAsync(string accountId, string loopId, int limit = 250, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CommuteProfileRecord>>(Items.Where(x => x.LoopId == loopId).ToArray());
        public Task<CommuteProfileRecord> UpsertAsync(string accountId, CommuteProfileRecord value, CancellationToken cancellationToken = default) { Items.RemoveAll(x => x.Id == value.Id); Items.Add(value); return Task.FromResult(value); }
        public Task<bool> DeleteAsync(string accountId, string loopId, string profileId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
    private sealed class ArtifactCalendarRepository(params CalendarEventRecord[] values) : ICalendarEventRepository
    {
        internal List<CalendarEventRecord> Items { get; } = [.. values]; internal void Clear() => Items.Clear();
        public Task<IReadOnlyList<CalendarEventRecord>> ListAsync(string accountId, string loopId, DateOnly? from = null, DateOnly? to = null, int limit = 500, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CalendarEventRecord>>(Items.Where(x => x.LoopId == loopId).ToArray());
        public Task<CalendarEventRecord> UpsertAsync(string accountId, CalendarEventRecord value, CancellationToken cancellationToken = default) { Items.RemoveAll(x => x.Id == value.Id); Items.Add(value); return Task.FromResult(value); }
        public Task<bool> DeleteAsync(string accountId, string loopId, string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
    private sealed class ArtifactGreetingRepository(params GreetingPresenceRecord[] values) : IGreetingPresenceRepository
    {
        internal List<GreetingPresenceRecord> Items { get; } = [.. values]; internal void Clear() => Items.Clear();
        public Task<IReadOnlyList<GreetingPresenceRecord>> ListAsync(string accountId, string loopId, int limit = 250, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GreetingPresenceRecord>>(Items.Where(x => x.AccountId == accountId && x.LoopId == loopId).ToArray());
        public Task<GreetingPresenceRecord> UpsertAsync(GreetingPresenceRecord value, CancellationToken cancellationToken = default) { Items.RemoveAll(x => x.Id == value.Id); Items.Add(value); return Task.FromResult(value); }
        public Task<bool> DeleteAsync(string accountId, string loopId, string presenceId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
