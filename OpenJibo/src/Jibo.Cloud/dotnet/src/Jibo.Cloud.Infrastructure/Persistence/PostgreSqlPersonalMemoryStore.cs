using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

/// <summary>
/// Stores personal memory as tenant-scoped relational rows. Only one tenant scope is
/// hydrated at a time and the bounded cache prevents the process from retaining the database.
/// </summary>
public sealed class PostgreSqlPersonalMemoryStore : IPersonalMemoryStore, IDisposable
{
    private const string CurrentSchemaVersion = "2";
    private const string LegacyImportName = "persistence-snapshot-v1";
    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _ownsDataSource;
    private readonly ScopedMemoryCache _cache;
    private readonly ITransportMetrics _metrics;
    private DateTimeOffset? _lastLoadedUtc;
    private DateTimeOffset? _lastSavedUtc;
    private long _revision;

    public PostgreSqlPersonalMemoryStore(NpgsqlDataSource dataSource, int cacheMaxEntries = 256,
        TimeSpan? cacheTtl = null, ITransportMetrics? transportMetrics = null)
    {
        _dataSource = dataSource;
        _metrics = transportMetrics ?? NullTransportMetrics.Instance;
        _cache = new ScopedMemoryCache(cacheMaxEntries, cacheTtl ?? TimeSpan.FromMinutes(5),
            result => _metrics.PersistenceCacheAccess("personal_memory", result));
        ImportLegacySnapshotOnce();
        LoadPersistedState();
    }

    public PostgreSqlPersonalMemoryStore(string connectionString, int maxPoolSize = 4,
        int cacheMaxEntries = 256, TimeSpan? cacheTtl = null, ITransportMetrics? transportMetrics = null)
        : this(CreateDataSource(connectionString, maxPoolSize), cacheMaxEntries, cacheTtl, transportMetrics)
    {
        _ownsDataSource = true;
    }

    public void Dispose()
    {
        if (_ownsDataSource) _dataSource.Dispose();
    }

    public PersistenceStateInfo GetPersistenceStateInfo() =>
        new(CurrentSchemaVersion, Interlocked.Read(ref _revision), _lastLoadedUtc, _lastSavedUtc);

    public void LoadPersistedState()
    {
        _cache.Clear();
        using var connection = _dataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Revision, UpdatedUtc FROM PersonalMemoryState WHERE StateKey = 'personal-memory'";
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            Interlocked.Exchange(ref _revision, reader.GetInt64(0));
            _lastSavedUtc = reader.GetFieldValue<DateTimeOffset>(1);
        }

        _lastLoadedUtc = DateTimeOffset.UtcNow;
    }

    // Relational writes are committed immediately; this method remains for interface compatibility.
    public void SavePersistedState() => _lastSavedUtc = DateTimeOffset.UtcNow;

    public void SetBirthday(PersonalMemoryTenantScope tenantScope, string birthdayText) =>
        UpsertProfile(tenantScope, "Birthday", birthdayText);

    public string? GetBirthday(PersonalMemoryTenantScope tenantScope) => LoadScope(tenantScope).Birthday;

    public void SetName(PersonalMemoryTenantScope tenantScope, string name) =>
        UpsertProfile(tenantScope, "Name", name);

    public string? GetName(PersonalMemoryTenantScope tenantScope) => LoadScope(tenantScope).Name;

    public void SetPreference(PersonalMemoryTenantScope tenantScope, string category, string value) =>
        UpsertFact(tenantScope, "PersonalMemoryPreferences", "Category", category, "Value", value);

    public string? GetPreference(PersonalMemoryTenantScope tenantScope, string category) =>
        LoadScope(tenantScope).Preferences.GetValueOrDefault(Normalize(category));

    public void SetImportantDate(PersonalMemoryTenantScope tenantScope, string label, string value) =>
        UpsertFact(tenantScope, "PersonalMemoryImportantDates", "Label", label, "Value", value);

    public string? GetImportantDate(PersonalMemoryTenantScope tenantScope, string label) =>
        LoadScope(tenantScope).ImportantDates.GetValueOrDefault(Normalize(label));

    public void SetAffinity(PersonalMemoryTenantScope tenantScope, string item, PersonalAffinity affinity) =>
        UpsertFact(tenantScope, "PersonalMemoryAffinities", "Item", item, "Affinity", affinity.ToString());

    public PersonalAffinity? GetAffinity(PersonalMemoryTenantScope tenantScope, string item)
    {
        var affinities = LoadScope(tenantScope).Affinities;
        return affinities.TryGetValue(Normalize(item), out var affinity) ? affinity : null;
    }

    public IReadOnlyDictionary<string, PersonalAffinity> GetAffinities(PersonalMemoryTenantScope tenantScope) =>
        new Dictionary<string, PersonalAffinity>(LoadScope(tenantScope).Affinities,
            StringComparer.OrdinalIgnoreCase);

    public void AddListItem(PersonalMemoryTenantScope tenantScope, string listName, string item)
    {
        var normalizedListName = Normalize(listName);
        var itemValue = item.Trim();
        if (normalizedListName.Length == 0 || itemValue.Length == 0) return;

        ExecuteMutation(tenantScope, (connection, transaction, scopeKey) =>
        {
            using var command = new NpgsqlCommand("""
                                                  INSERT INTO PersonalMemoryListItems
                                                      (ScopeKey, ListName, ItemKey, ItemValue)
                                                  VALUES (@scopeKey, @listName, @itemKey, @itemValue)
                                                  ON CONFLICT (ScopeKey, ListName, ItemKey) DO NOTHING
                                                  """, connection, transaction);
            command.Parameters.AddWithValue("scopeKey", scopeKey);
            command.Parameters.AddWithValue("listName", normalizedListName);
            command.Parameters.AddWithValue("itemKey", Normalize(itemValue));
            command.Parameters.AddWithValue("itemValue", itemValue);
            command.ExecuteNonQuery();
        });
    }

    public IReadOnlyList<string> GetListItems(PersonalMemoryTenantScope tenantScope, string listName) =>
        LoadScope(tenantScope).Lists.GetValueOrDefault(Normalize(listName))?.ToArray() ?? [];

    public void ClearListItems(PersonalMemoryTenantScope tenantScope, string listName)
    {
        ExecuteMutation(tenantScope, (connection, transaction, scopeKey) =>
        {
            using var command = new NpgsqlCommand("""
                                                  DELETE FROM PersonalMemoryListItems
                                                  WHERE ScopeKey = @scopeKey AND ListName = @listName
                                                  """, connection, transaction);
            command.Parameters.AddWithValue("scopeKey", scopeKey);
            command.Parameters.AddWithValue("listName", Normalize(listName));
            command.ExecuteNonQuery();
        });
    }

    private TenantMemoryRecord LoadScope(PersonalMemoryTenantScope scope)
    {
        var scopeKey = BuildScopeKey(scope);
        return _cache.GetOrAdd(scopeKey, () => ReadScope(scopeKey));
    }

    private TenantMemoryRecord ReadScope(string scopeKey)
    {
        var result = new TenantMemoryRecord();
        using var connection = _dataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT Name, Birthday FROM PersonalMemoryProfiles WHERE ScopeKey = @scopeKey;
                              SELECT Category, Value FROM PersonalMemoryPreferences WHERE ScopeKey = @scopeKey;
                              SELECT Label, Value FROM PersonalMemoryImportantDates WHERE ScopeKey = @scopeKey;
                              SELECT Item, Affinity FROM PersonalMemoryAffinities WHERE ScopeKey = @scopeKey;
                              SELECT ListName, ItemValue FROM PersonalMemoryListItems
                                  WHERE ScopeKey = @scopeKey ORDER BY ListName, ItemId;
                              """;
        command.Parameters.AddWithValue("scopeKey", scopeKey);
        using var reader = command.ExecuteReader();

        if (reader.Read())
        {
            result.Name = reader.IsDBNull(0) ? null : reader.GetString(0);
            result.Birthday = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        reader.NextResult();
        while (reader.Read()) result.Preferences[reader.GetString(0)] = reader.GetString(1);
        reader.NextResult();
        while (reader.Read()) result.ImportantDates[reader.GetString(0)] = reader.GetString(1);
        reader.NextResult();
        while (reader.Read())
            if (Enum.TryParse<PersonalAffinity>(reader.GetString(1), true, out var affinity))
                result.Affinities[reader.GetString(0)] = affinity;
        reader.NextResult();
        while (reader.Read())
        {
            var listName = reader.GetString(0);
            if (!result.Lists.TryGetValue(listName, out var items))
                result.Lists[listName] = items = [];
            items.Add(reader.GetString(1));
        }

        return result;
    }

    private void UpsertProfile(PersonalMemoryTenantScope scope, string columnName, string value)
    {
        ExecuteMutation(scope, (connection, transaction, scopeKey) =>
        {
            using var command = new NpgsqlCommand($"""
                                                   INSERT INTO PersonalMemoryProfiles (ScopeKey, {columnName})
                                                   VALUES (@scopeKey, @value)
                                                   ON CONFLICT (ScopeKey) DO UPDATE SET
                                                       {columnName} = EXCLUDED.{columnName}, UpdatedUtc = NOW()
                                                   """, connection, transaction);
            command.Parameters.AddWithValue("scopeKey", scopeKey);
            command.Parameters.AddWithValue("value", value);
            command.ExecuteNonQuery();
        });
    }

    private void UpsertFact(PersonalMemoryTenantScope scope, string tableName, string keyColumn,
        string key, string valueColumn, string value)
    {
        var normalizedKey = Normalize(key);
        ExecuteMutation(scope, (connection, transaction, scopeKey) =>
        {
            using var command = new NpgsqlCommand($"""
                                                   INSERT INTO {tableName} (ScopeKey, {keyColumn}, {valueColumn})
                                                   VALUES (@scopeKey, @key, @value)
                                                   ON CONFLICT (ScopeKey, {keyColumn}) DO UPDATE SET
                                                       {valueColumn} = EXCLUDED.{valueColumn}, UpdatedUtc = NOW()
                                                   """, connection, transaction);
            command.Parameters.AddWithValue("scopeKey", scopeKey);
            command.Parameters.AddWithValue("key", normalizedKey);
            command.Parameters.AddWithValue("value", value);
            command.ExecuteNonQuery();
        });
    }

    private void ExecuteMutation(PersonalMemoryTenantScope scope,
        Action<NpgsqlConnection, NpgsqlTransaction, string> mutation)
    {
        var scopeKey = BuildScopeKey(scope);
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureScope(connection, transaction, scopeKey, scope);
        mutation(connection, transaction, scopeKey);
        var revision = BumpRevision(connection, transaction);
        transaction.Commit();
        Interlocked.Exchange(ref _revision, revision);
        _lastSavedUtc = DateTimeOffset.UtcNow;
        _cache.Remove(scopeKey);
    }

    private static void EnsureScope(NpgsqlConnection connection, NpgsqlTransaction transaction, string scopeKey,
        PersonalMemoryTenantScope scope)
    {
        using var command = new NpgsqlCommand("""
                                              INSERT INTO PersonalMemoryScopes
                                                  (ScopeKey, AccountId, LoopId, DeviceId, PersonId)
                                              VALUES (@scopeKey, @accountId, @loopId, @deviceId, @personId)
                                              ON CONFLICT (ScopeKey) DO UPDATE SET UpdatedUtc = NOW()
                                              """, connection, transaction);
        command.Parameters.AddWithValue("scopeKey", scopeKey);
        command.Parameters.AddWithValue("accountId", scope.AccountId);
        command.Parameters.AddWithValue("loopId", scope.LoopId);
        command.Parameters.AddWithValue("deviceId", scope.DeviceId);
        command.Parameters.Add("personId", NpgsqlDbType.Text).Value = (object?)scope.PersonId ?? DBNull.Value;
        command.ExecuteNonQuery();
    }

    private static long BumpRevision(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        using var command = new NpgsqlCommand("""
                                              UPDATE PersonalMemoryState
                                              SET Revision = Revision + 1, UpdatedUtc = NOW()
                                              WHERE StateKey = 'personal-memory'
                                              RETURNING Revision
                                              """, connection, transaction);
        return (long)(command.ExecuteScalar() ?? throw new InvalidOperationException(
            "PersonalMemoryState is missing. Run the PostgreSQL migrations before starting the service."));
    }

    private void ImportLegacySnapshotOnce()
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var importLock = new NpgsqlCommand(
                   "SELECT pg_advisory_xact_lock(hashtext(@name))", connection, transaction))
        {
            importLock.Parameters.AddWithValue("name", LegacyImportName);
            importLock.ExecuteNonQuery();
        }

        using (var check = new NpgsqlCommand(
                   "SELECT 1 FROM PersonalMemoryImports WHERE ImportName = @name", connection, transaction))
        {
            check.Parameters.AddWithValue("name", LegacyImportName);
            if (check.ExecuteScalar() is not null)
            {
                transaction.Commit();
                return;
            }
        }

        LegacySnapshot? snapshot = null;
        using (var load = new NpgsqlCommand("""
                                           SELECT SnapshotJson FROM PersistenceSnapshots
                                           WHERE SnapshotName = 'personal-memory'
                                           """, connection, transaction))
        {
            if (load.ExecuteScalar() is string json)
            {
                try
                {
                    snapshot = JsonSerializer.Deserialize<LegacySnapshot>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException)
                {
                    // Leave malformed legacy data untouched and mark no import, allowing repair and retry.
                    throw new InvalidOperationException(
                        "The legacy personal-memory snapshot is not valid JSON and could not be imported.");
                }
            }
        }

        var imported = 0;
        foreach (var tenant in snapshot?.Tenants ?? [])
        {
            if (!TryParseLegacyScope(tenant.TenantKey, out var scope)) continue;
            ImportTenant(connection, transaction, scope, tenant);
            imported++;
        }

        using (var mark = new NpgsqlCommand("""
                                           INSERT INTO PersonalMemoryImports
                                               (ImportName, TenantCount, SourceRevision)
                                           VALUES (@name, @tenantCount, @revision)
                                           """, connection, transaction))
        {
            mark.Parameters.AddWithValue("name", LegacyImportName);
            mark.Parameters.AddWithValue("tenantCount", imported);
            mark.Parameters.AddWithValue("revision", snapshot?.Revision ?? 0);
            mark.ExecuteNonQuery();
        }

        using (var revision = new NpgsqlCommand("""
                                               UPDATE PersonalMemoryState
                                               SET Revision = GREATEST(Revision, @revision), UpdatedUtc = NOW()
                                               WHERE StateKey = 'personal-memory'
                                               """, connection, transaction))
        {
            revision.Parameters.AddWithValue("revision", snapshot?.Revision ?? 0);
            revision.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void ImportTenant(NpgsqlConnection connection, NpgsqlTransaction transaction,
        PersonalMemoryTenantScope scope, LegacyTenant tenant)
    {
        var scopeKey = BuildScopeKey(scope);
        EnsureScope(connection, transaction, scopeKey, scope);
        if (tenant.Name is not null) ImportProfileValue(connection, transaction, scopeKey, "Name", tenant.Name);
        if (tenant.Birthday is not null)
            ImportProfileValue(connection, transaction, scopeKey, "Birthday", tenant.Birthday);
        foreach (var item in tenant.Preferences)
            ImportFact(connection, transaction, "PersonalMemoryPreferences", "Category", "Value", scopeKey,
                item.Key, item.Value);
        foreach (var item in tenant.ImportantDates)
            ImportFact(connection, transaction, "PersonalMemoryImportantDates", "Label", "Value", scopeKey,
                item.Key, item.Value);
        foreach (var item in tenant.Affinities)
            ImportFact(connection, transaction, "PersonalMemoryAffinities", "Item", "Affinity", scopeKey,
                item.Key, item.Value.ToString());
        foreach (var list in tenant.Lists)
            foreach (var item in list.Value)
                ImportListItem(connection, transaction, scopeKey, list.Key, item);
    }

    private static void ImportProfileValue(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string scopeKey, string column, string value)
    {
        using var command = new NpgsqlCommand($"""
                                               INSERT INTO PersonalMemoryProfiles (ScopeKey, {column})
                                               VALUES (@scopeKey, @value)
                                               ON CONFLICT (ScopeKey) DO UPDATE SET {column} = EXCLUDED.{column}
                                               """, connection, transaction);
        command.Parameters.AddWithValue("scopeKey", scopeKey);
        command.Parameters.AddWithValue("value", value);
        command.ExecuteNonQuery();
    }

    private static void ImportFact(NpgsqlConnection connection, NpgsqlTransaction transaction, string table,
        string keyColumn, string valueColumn, string scopeKey, string key, string value)
    {
        using var command = new NpgsqlCommand($"""
                                               INSERT INTO {table} (ScopeKey, {keyColumn}, {valueColumn})
                                               VALUES (@scopeKey, @key, @value)
                                               ON CONFLICT (ScopeKey, {keyColumn}) DO NOTHING
                                               """, connection, transaction);
        command.Parameters.AddWithValue("scopeKey", scopeKey);
        command.Parameters.AddWithValue("key", Normalize(key));
        command.Parameters.AddWithValue("value", value);
        command.ExecuteNonQuery();
    }

    private static void ImportListItem(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string scopeKey, string listName, string item)
    {
        using var command = new NpgsqlCommand("""
                                              INSERT INTO PersonalMemoryListItems
                                                  (ScopeKey, ListName, ItemKey, ItemValue)
                                              VALUES (@scopeKey, @listName, @itemKey, @itemValue)
                                              ON CONFLICT (ScopeKey, ListName, ItemKey) DO NOTHING
                                              """, connection, transaction);
        command.Parameters.AddWithValue("scopeKey", scopeKey);
        command.Parameters.AddWithValue("listName", Normalize(listName));
        command.Parameters.AddWithValue("itemKey", Normalize(item));
        command.Parameters.AddWithValue("itemValue", item.Trim());
        command.ExecuteNonQuery();
    }

    internal static string BuildScopeKey(PersonalMemoryTenantScope scope) =>
        string.Join("|", scope.AccountId, scope.LoopId, scope.DeviceId, scope.PersonId ?? string.Empty);

    private static NpgsqlDataSource CreateDataSource(string connectionString, int maxPoolSize)
    {
        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = Math.Max(1, maxPoolSize),
            ApplicationName = "OpenJibo.PersonalMemory"
        };
        return NpgsqlDataSource.Create(connectionBuilder.ConnectionString);
    }

    private static bool TryParseLegacyScope(string tenantKey, out PersonalMemoryTenantScope scope)
    {
        var parts = tenantKey.Split('|');
        if (parts.Length is 3 or 4 && parts.Take(3).All(part => !string.IsNullOrWhiteSpace(part)))
        {
            scope = new PersonalMemoryTenantScope(parts[0], parts[1], parts[2],
                parts.Length == 4 && parts[3].Length > 0 ? parts[3] : null);
            return true;
        }

        scope = null!;
        return false;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private sealed class TenantMemoryRecord
    {
        public string? Birthday { get; set; }
        public string? Name { get; set; }
        public Dictionary<string, string> Preferences { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ImportantDates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, PersonalAffinity> Affinities { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ScopedMemoryCache(int maxEntries, TimeSpan ttl, Action<string>? recordAccess = null)
    {
        private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _syncRoot = new();
        private readonly int _maxEntries = Math.Max(1, maxEntries);
        private readonly TimeSpan _ttl = ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(5);

        public TenantMemoryRecord GetOrAdd(string key, Func<TenantMemoryRecord> factory)
        {
            var now = DateTimeOffset.UtcNow;
            lock (_syncRoot)
            {
                if (_entries.TryGetValue(key, out var cached) && cached.ExpiresUtc > now)
                {
                    cached.LastAccessUtc = now;
                    recordAccess?.Invoke("hit");
                    return cached.Value;
                }

                _entries.Remove(key);
            }

            recordAccess?.Invoke("miss");

            // Do not hold the cache lock during database I/O. Concurrent misses may
            // perform the same bounded scoped read, but unrelated tenants never block.
            var value = factory();
            lock (_syncRoot)
            {
                now = DateTimeOffset.UtcNow;
                if (_entries.TryGetValue(key, out var populated) && populated.ExpiresUtc > now)
                    return populated.Value;

                if (_entries.Count >= _maxEntries)
                {
                    var oldest = _entries.MinBy(pair => pair.Value.LastAccessUtc).Key;
                    _entries.Remove(oldest);
                }

                _entries[key] = new CacheEntry(value, now + _ttl, now);
                return value;
            }
        }

        public void Remove(string key)
        {
            lock (_syncRoot) _entries.Remove(key);
        }

        public void Clear()
        {
            lock (_syncRoot) _entries.Clear();
        }

        private sealed class CacheEntry(TenantMemoryRecord value, DateTimeOffset expiresUtc,
            DateTimeOffset lastAccessUtc)
        {
            public TenantMemoryRecord Value { get; } = value;
            public DateTimeOffset ExpiresUtc { get; } = expiresUtc;
            public DateTimeOffset LastAccessUtc { get; set; } = lastAccessUtc;
        }
    }

    private sealed class LegacySnapshot
    {
        public long Revision { get; init; }
        public LegacyTenant[]? Tenants { get; init; }
    }

    private sealed class LegacyTenant
    {
        public string TenantKey { get; init; } = string.Empty;
        public string? Birthday { get; init; }
        public string? Name { get; init; }
        public Dictionary<string, string> Preferences { get; init; } = [];
        public Dictionary<string, string> ImportantDates { get; init; } = [];
        public Dictionary<string, PersonalAffinity> Affinities { get; init; } = [];
        public Dictionary<string, string[]> Lists { get; init; } = [];
    }
}
