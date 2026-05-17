using System.Collections.Concurrent;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class InMemoryPersonalMemoryStore : IPersonalMemoryStore
{
    private const string CurrentSchemaVersion = "1";

    private static readonly JsonSerializerOptions PersistenceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ISnapshotStore _snapshotStore;
    private readonly Lock _syncRoot = new();

    private readonly ConcurrentDictionary<string, TenantMemoryRecord> _tenantMemory =
        new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset? _lastLoadedUtc;
    private DateTimeOffset? _lastSavedUtc;
    private long _revision;

    public InMemoryPersonalMemoryStore(string? persistencePath = null)
        : this(new JsonFileSnapshotStore(persistencePath, PersistenceJsonOptions))
    {
    }

    public InMemoryPersonalMemoryStore(ISnapshotStore snapshotStore)
    {
        _snapshotStore = snapshotStore;
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

        _tenantMemory.Clear();
        foreach (var tenant in snapshot.Tenants ?? []) _tenantMemory[tenant.TenantKey] = tenant.ToRecord();

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
                Tenants = _tenantMemory
                    .Select(pair => new TenantMemorySnapshot
                    {
                        TenantKey = pair.Key,
                        Birthday = pair.Value.Birthday,
                        Name = pair.Value.Name,
                        Preferences = pair.Value.Preferences.ToDictionary(entry => entry.Key, entry => entry.Value,
                            StringComparer.OrdinalIgnoreCase),
                        ImportantDates = pair.Value.ImportantDates.ToDictionary(entry => entry.Key,
                            entry => entry.Value, StringComparer.OrdinalIgnoreCase),
                        Affinities = pair.Value.Affinities.ToDictionary(entry => entry.Key, entry => entry.Value,
                            StringComparer.OrdinalIgnoreCase),
                        Lists = pair.Value.Lists.ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value.ToArray(),
                            StringComparer.OrdinalIgnoreCase)
                    })
                    .ToArray()
            };
            _snapshotStore.Save(snapshot);
            _lastSavedUtc = now;
        }
    }

    public void SetBirthday(PersonalMemoryTenantScope tenantScope, string birthdayText)
    {
        var record = GetOrCreateTenantRecord(tenantScope);
        record.Birthday = birthdayText;
        TouchState();
    }

    public string? GetBirthday(PersonalMemoryTenantScope tenantScope)
    {
        var key = BuildTenantKey(tenantScope);
        return _tenantMemory.TryGetValue(key, out var record) ? record.Birthday : null;
    }

    public void SetPreference(PersonalMemoryTenantScope tenantScope, string category, string value)
    {
        var record = GetOrCreateTenantRecord(tenantScope);
        record.Preferences[NormalizeCategory(category)] = value;
        TouchState();
    }

    public string? GetPreference(PersonalMemoryTenantScope tenantScope, string category)
    {
        var key = BuildTenantKey(tenantScope);
        return _tenantMemory.TryGetValue(key, out var record) &&
               record.Preferences.TryGetValue(NormalizeCategory(category), out var value)
            ? value
            : null;
    }

    public void SetName(PersonalMemoryTenantScope tenantScope, string name)
    {
        var record = GetOrCreateTenantRecord(tenantScope);
        record.Name = name;
        TouchState();
    }

    public string? GetName(PersonalMemoryTenantScope tenantScope)
    {
        var key = BuildTenantKey(tenantScope);
        return _tenantMemory.TryGetValue(key, out var record) ? record.Name : null;
    }

    public void SetImportantDate(PersonalMemoryTenantScope tenantScope, string label, string value)
    {
        var record = GetOrCreateTenantRecord(tenantScope);
        record.ImportantDates[NormalizeCategory(label)] = value;
        TouchState();
    }

    public string? GetImportantDate(PersonalMemoryTenantScope tenantScope, string label)
    {
        var key = BuildTenantKey(tenantScope);
        return _tenantMemory.TryGetValue(key, out var record) &&
               record.ImportantDates.TryGetValue(NormalizeCategory(label), out var value)
            ? value
            : null;
    }

    public void SetAffinity(PersonalMemoryTenantScope tenantScope, string item, PersonalAffinity affinity)
    {
        var record = GetOrCreateTenantRecord(tenantScope);
        record.Affinities[NormalizeCategory(item)] = affinity;
        TouchState();
    }

    public PersonalAffinity? GetAffinity(PersonalMemoryTenantScope tenantScope, string item)
    {
        var key = BuildTenantKey(tenantScope);
        return _tenantMemory.TryGetValue(key, out var record) &&
               record.Affinities.TryGetValue(NormalizeCategory(item), out var affinity)
            ? affinity
            : null;
    }

    public IReadOnlyDictionary<string, PersonalAffinity> GetAffinities(PersonalMemoryTenantScope tenantScope)
    {
        var key = BuildTenantKey(tenantScope);
        return !_tenantMemory.TryGetValue(key, out var record)
            ? new Dictionary<string, PersonalAffinity>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, PersonalAffinity>(record.Affinities, StringComparer.OrdinalIgnoreCase);
    }

    public void AddListItem(PersonalMemoryTenantScope tenantScope, string listName, string item)
    {
        var normalizedListName = NormalizeCategory(listName);
        var normalizedItem = item.Trim();
        if (string.IsNullOrWhiteSpace(normalizedListName) || string.IsNullOrWhiteSpace(normalizedItem)) return;

        var record = GetOrCreateTenantRecord(tenantScope);
        var changed = false;
        lock (record.SyncRoot)
        {
            var list = record.Lists.GetOrAdd(normalizedListName, static _ => []);
            if (list.Any(value => string.Equals(value, normalizedItem, StringComparison.OrdinalIgnoreCase))) return;

            list.Add(normalizedItem);
            changed = true;
        }

        if (changed) TouchState();
    }

    public IReadOnlyList<string> GetListItems(PersonalMemoryTenantScope tenantScope, string listName)
    {
        var key = BuildTenantKey(tenantScope);
        if (!_tenantMemory.TryGetValue(key, out var record)) return [];

        var normalizedListName = NormalizeCategory(listName);
        lock (record.SyncRoot)
        {
            return record.Lists.TryGetValue(normalizedListName, out var list)
                ? [.. list]
                : [];
        }
    }

    public void ClearListItems(PersonalMemoryTenantScope tenantScope, string listName)
    {
        var key = BuildTenantKey(tenantScope);
        if (!_tenantMemory.TryGetValue(key, out var record)) return;

        var changed = false;
        lock (record.SyncRoot)
        {
            changed = record.Lists.TryRemove(NormalizeCategory(listName), out _);
        }

        if (changed) TouchState();
    }

    private TenantMemoryRecord GetOrCreateTenantRecord(PersonalMemoryTenantScope tenantScope)
    {
        var key = BuildTenantKey(tenantScope);
        return _tenantMemory.GetOrAdd(key, static _ => new TenantMemoryRecord());
    }

    private void TouchState()
    {
        Interlocked.Increment(ref _revision);
        SavePersistedState();
    }

    private static string BuildTenantKey(PersonalMemoryTenantScope tenantScope)
    {
        return string.IsNullOrWhiteSpace(tenantScope.PersonId)
            ? $"{tenantScope.AccountId}|{tenantScope.LoopId}|{tenantScope.DeviceId}"
            : $"{tenantScope.AccountId}|{tenantScope.LoopId}|{tenantScope.DeviceId}|{tenantScope.PersonId}";
    }

    private static string NormalizeCategory(string category)
    {
        return category.Trim().ToLowerInvariant();
    }

    private sealed class TenantMemoryRecord
    {
        public string? Birthday { get; set; }
        public string? Name { get; set; }
        public ConcurrentDictionary<string, string> Preferences { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, string> ImportantDates { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ConcurrentDictionary<string, PersonalAffinity> Affinities { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public ConcurrentDictionary<string, List<string>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);
        public object SyncRoot { get; } = new();
    }

    private sealed class PersistentStateSnapshot
    {
        public string SchemaVersion { get; init; } = CurrentSchemaVersion;
        public long Revision { get; init; }
        public DateTimeOffset? LastLoadedUtc { get; init; }
        public DateTimeOffset? LastSavedUtc { get; init; }
        public TenantMemorySnapshot[]? Tenants { get; init; }
    }

    private sealed class TenantMemorySnapshot
    {
        public string TenantKey { get; init; } = string.Empty;
        public string? Birthday { get; init; }
        public string? Name { get; init; }

        public IDictionary<string, string> Preferences { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IDictionary<string, string> ImportantDates { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IDictionary<string, PersonalAffinity> Affinities { get; init; } =
            new Dictionary<string, PersonalAffinity>(StringComparer.OrdinalIgnoreCase);

        public IDictionary<string, string[]> Lists { get; init; } =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        public TenantMemoryRecord ToRecord()
        {
            var record = new TenantMemoryRecord
            {
                Birthday = Birthday,
                Name = Name
            };

            foreach (var preference in Preferences) record.Preferences[preference.Key] = preference.Value;

            foreach (var date in ImportantDates) record.ImportantDates[date.Key] = date.Value;

            foreach (var affinity in Affinities) record.Affinities[affinity.Key] = affinity.Value;

            foreach (var list in Lists) record.Lists[list.Key] = [.. list.Value];

            return record;
        }
    }
}