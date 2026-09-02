using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PostgreSqlCloudStateBootstrapTests
{
    [Fact]
    public void LoadPersistedState_SeedsEmptyStoreOnceWithoutReplacingExistingTopology()
    {
        var accounts = new EmptyAccountRepository();
        var devices = new EmptyDeviceRepository();
        var loops = new CollectingLoopRepository();
        var people = new CollectingPersonRepository();
        var profiles = new CollectingRobotProfileRepository();
        var store = new PostgreSqlCloudStateStore(new MetadataRepository(), accounts, devices, null!, null!,
            new BoundedCloudSessionRegistry(4), loops: loops, members: new MemberRepository(), people: people,
            robotProfiles: profiles, ownerFirstName: "Ada", ownerLastName: "Lovelace");

        store.LoadPersistedState();
        store.LoadPersistedState();

        Assert.Equal("Ada", accounts.Account!.FirstName);
        Assert.Equal("Lovelace", accounts.Account.LastName);
        Assert.Equal(RobotRegistrationSources.Bootstrap, devices.Device!.RegistrationSource);
        Assert.True(devices.Device.IsHidden);
        Assert.Single(loops.Items);
        Assert.Equal(2, people.Items.Count);
        Assert.Equal(1, accounts.UpsertCount);
        Assert.Equal(1, devices.UpsertCount);
        Assert.Equal(1, profiles.UpsertCount);
    }

    [Fact]
    public void LoadPersistedState_RefusesToBootstrapOverUnimportedLegacySnapshot()
    {
        var store = new PostgreSqlCloudStateStore(new MetadataRepository(hasLegacySnapshot: true),
            new EmptyAccountRepository(), new EmptyDeviceRepository(), null!, null!,
            new BoundedCloudSessionRegistry(4));

        var exception = Assert.Throws<InvalidOperationException>(() => store.LoadPersistedState());

        Assert.Contains("--import-legacy-cloud-state", exception.Message, StringComparison.Ordinal);
    }

    private sealed class MetadataRepository(bool hasLegacySnapshot = false) : ICloudStateMetadataRepository
    {
        public Task<CloudStateMetadataRecord> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CloudStateMetadataRecord(4, 0, DateTimeOffset.UtcNow));
        public Task<bool> HasLegacySnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(hasLegacySnapshot);
    }

    private sealed class EmptyAccountRepository : ICloudAccountRepository
    {
        internal AccountProfile? Account { get; private set; }
        internal int UpsertCount { get; private set; }
        public Task<AccountProfile?> GetByIdAsync(string accountId, CancellationToken cancellationToken = default) => Task.FromResult(Account?.AccountId == accountId ? Account : null);
        public Task<AccountProfile?> GetDefaultAsync(CancellationToken cancellationToken = default) => Task.FromResult(Account);
        public Task<AccountProfile> UpsertAsync(AccountProfile account, bool? isDefault = null, CancellationToken cancellationToken = default)
        { Account = account; UpsertCount++; return Task.FromResult(account); }
    }

    private sealed class EmptyDeviceRepository : ICloudDeviceRepository
    {
        internal DeviceRegistration? Device { get; private set; }
        internal int UpsertCount { get; private set; }
        public Task<DeviceRegistration?> GetByDeviceIdAsync(string deviceId, CancellationToken cancellationToken = default) => Task.FromResult(Device?.DeviceId == deviceId ? Device : null);
        public Task<DeviceRegistration?> FindByFriendlyIdAsync(string friendlyId, CancellationToken cancellationToken = default) => Task.FromResult(Device);
        public Task<IReadOnlyList<DeviceRegistration>> FindVisibleIdentityCandidatesAsync(string accountId, string identity, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceRegistration>>(Device is null || Device.IsHidden || Device.ArchivedUtc is not null ? [] : [Device]);
        public Task<DeviceRegistration?> GetDefaultAsync(CancellationToken cancellationToken = default) => Task.FromResult(Device);
        public Task<IReadOnlyList<DeviceRegistration>> ListForAccountAsync(string accountId, bool includeArchived = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeviceRegistration>>(Device is null ? [] : [Device]);
        public Task<IReadOnlyList<DeviceRegistration>> ListAllAsync(bool includeArchived = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeviceRegistration>>(Device is null ? [] : [Device]);
        public Task<IReadOnlyList<string>> ListAccountIdsAsync(string deviceId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(["test-account"]);
        public Task<DeviceRegistration> UpsertAsync(DeviceRegistration device, string? accountId = null, bool? isDefault = null, CancellationToken cancellationToken = default)
        { Device = device; UpsertCount++; return Task.FromResult(device); }
        public Task<RobotCredentialBinding?> GetCredentialBindingAsync(string accessKeyFingerprint, CancellationToken cancellationToken = default) => Task.FromResult<RobotCredentialBinding?>(null);
        public Task<IReadOnlyList<RobotCredentialBinding>> ListCredentialBindingsAsync(string deviceId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RobotCredentialBinding>>([]);
        public Task<IReadOnlyList<RobotCredentialBinding>> ListCredentialBindingsForAccountAsync(string accountId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RobotCredentialBinding>>([]);
        public Task<RobotCredentialBinding> BindCredentialAsync(string deviceId, string accessKeyFingerprint, string claimSource, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RobotCredentialBinding>> SwapCredentialBindingsAsync(string firstAccessKeyFingerprint, string secondAccessKeyFingerprint, string claimSource, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeviceRegistration?> FindByCredentialFingerprintAsync(string accessKeyFingerprint, CancellationToken cancellationToken = default) => Task.FromResult<DeviceRegistration?>(null);
        public Task<int> MoveCredentialBindingsAsync(string sourceDeviceId, string targetDeviceId, string claimSource, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class CollectingLoopRepository : ILoopTopologyRepository
    {
        internal List<StoredLoopTopology> Items { get; } = [];
        public Task<IReadOnlyList<StoredLoopTopology>> ListForAccountAsync(string accountId, int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoredLoopTopology>>(Items);
        public Task<IReadOnlyList<StoredLoopTopology>> ListForDeviceAsync(string accountId, string deviceId, int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoredLoopTopology>>(Items);
        public Task<StoredLoopTopology?> GetAsync(string accountId, string loopId, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.Loop.LoopId == loopId));
        public Task<StoredLoopTopology> UpsertAsync(StoredLoopTopology topology, CancellationToken cancellationToken = default) { Items.Add(topology); return Task.FromResult(topology); }
        public Task<bool> DeleteAsync(string accountId, string loopId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class MemberRepository : ILoopMemberRepository
    {
        public Task<IReadOnlyList<LoopMemberRecord>> ListAsync(string accountId, string loopId, int limit = 250, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LoopMemberRecord>>([]);
        public Task<LoopMemberRecord?> GetAsync(string accountId, string loopId, string memberId, CancellationToken cancellationToken = default) => Task.FromResult<LoopMemberRecord?>(null);
        public Task<LoopMemberRecord> UpsertAsync(string accountId, LoopMemberRecord member, CancellationToken cancellationToken = default) => Task.FromResult(member);
        public Task<bool> DeleteAsync(string accountId, string loopId, string memberId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class CollectingPersonRepository : IPersonRepository
    {
        internal List<PersonRecord> Items { get; } = [];
        public Task<IReadOnlyList<PersonRecord>> ListAsync(string accountId, string loopId, int limit = 250, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PersonRecord>>(Items);
        public Task<PersonRecord> UpsertAsync(PersonRecord person, CancellationToken cancellationToken = default) { Items.Add(person); return Task.FromResult(person); }
        public Task<bool> DeleteAsync(string accountId, string loopId, string personId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class CollectingRobotProfileRepository : IRobotProfileRepository
    {
        internal int UpsertCount { get; private set; }
        public Task<RobotProfile?> GetAsync(string robotId, CancellationToken cancellationToken = default) => Task.FromResult<RobotProfile?>(null);
        public Task<RobotProfile> UpsertAsync(RobotProfile profile, string? deviceId, CancellationToken cancellationToken = default) { UpsertCount++; return Task.FromResult(profile); }
    }
}
