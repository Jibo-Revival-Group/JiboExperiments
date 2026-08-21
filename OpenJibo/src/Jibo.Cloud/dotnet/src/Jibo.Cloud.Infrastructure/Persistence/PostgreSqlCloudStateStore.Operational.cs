using System.Security.Cryptography;
using System.Text;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed partial class PostgreSqlCloudStateStore
{
    public bool ShouldCreateSymmetricKey(string loopId) =>
        Sync(Required(_loopKeys, nameof(ShouldCreateSymmetricKey)).GetAsync(GetAccount().AccountId,
            OperationalLoop(loopId))) is null;

    public string GetOrCreateSymmetricKey(string loopId)
    {
        var repository = Required(_loopKeys, nameof(GetOrCreateSymmetricKey));
        var resolvedLoop = OperationalLoop(loopId);
        var existing = Sync(repository.GetAsync(GetAccount().AccountId, resolvedLoop));
        if (existing is not null) return Encoding.UTF8.GetString(existing.EncryptedKey);
        var value = Convert.ToBase64String(Encoding.UTF8.GetBytes($"open-jibo-symmetric-key:{resolvedLoop}"));
        Sync(repository.UpsertAsync(GetAccount().AccountId,
            new LoopSymmetricKeyRecord(resolvedLoop, Encoding.UTF8.GetBytes(value), "none", "legacy-base64",
                DateTimeOffset.UtcNow)));
        return value;
    }

    public KeyRequestRecord CreateKeyRequest(string loopId, string publicKey)
    {
        var record = new KeyRequestRecord
        {
            RequestId = $"req-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            LoopId = OperationalLoop(loopId), PublicKey = publicKey ?? string.Empty
        };
        return Sync(Required(_loopKeys, nameof(CreateKeyRequest)).UpsertRequestAsync(GetAccount().AccountId,
            new StoredKeyRequest(record))).Request;
    }

    public KeyRequestRecord GetKeyRequest(string loopId, string? requestId, string? publicKey)
    {
        var resolvedLoop = OperationalLoop(loopId);
        var match = Sync(Required(_loopKeys, nameof(GetKeyRequest))
                .ListRequestsAsync(GetAccount().AccountId, resolvedLoop, 500))
            .FirstOrDefault(item =>
                (!string.IsNullOrWhiteSpace(requestId) && item.Request.RequestId == requestId) ||
                (!string.IsNullOrWhiteSpace(publicKey) && item.Request.PublicKey == publicKey));
        return match?.Request ?? new KeyRequestRecord
        {
            RequestId = requestId ?? "unknown-request", LoopId = resolvedLoop, PublicKey = publicKey ?? string.Empty
        };
    }

    public IReadOnlyList<KeyRequestRecord> GetIncomingKeyRequests() => [];
    public IReadOnlyList<KeyRequestRecord> GetBinaryRequests() => [];
    public IReadOnlyList<HolidayRecord> GetHolidays(string? loopId = null) =>
        Sync(Required(_holidays, nameof(GetHolidays)).ListAsync(GetAccount().AccountId, OperationalLoop(loopId), 1000))
            .Where(item => item.IsEnabled).ToArray();
    public HolidayRecord UpsertHoliday(HolidayRecord holiday) =>
        Sync(Required(_holidays, nameof(UpsertHoliday)).UpsertAsync(GetAccount().AccountId, holiday));
    public IReadOnlyList<CommuteProfileRecord> GetCommuteProfiles(string? loopId = null) =>
        Sync(Required(_commutes, nameof(GetCommuteProfiles)).ListAsync(GetAccount().AccountId,
            OperationalLoop(loopId), 1000));
    public CommuteProfileRecord UpsertCommuteProfile(CommuteProfileRecord commuteProfile) =>
        Sync(Required(_commutes, nameof(UpsertCommuteProfile)).UpsertAsync(GetAccount().AccountId, commuteProfile));
    public IReadOnlyList<CalendarEventRecord> GetCalendarEvents(string? loopId = null) =>
        Sync(Required(_calendar, nameof(GetCalendarEvents)).ListAsync(GetAccount().AccountId,
            OperationalLoop(loopId), limit: 2000)).Where(item => item.IsEnabled).ToArray();
    public CalendarEventRecord UpsertCalendarEvent(CalendarEventRecord calendarEvent) =>
        Sync(Required(_calendar, nameof(UpsertCalendarEvent)).UpsertAsync(GetAccount().AccountId, calendarEvent));
    public IReadOnlyList<GreetingPresenceRecord> GetGreetingPresences(string? loopId = null) =>
        Sync(Required(_greetings, nameof(GetGreetingPresences)).ListAsync(GetAccount().AccountId,
            OperationalLoop(loopId), 1000));
    public GreetingPresenceRecord UpsertGreetingPresence(GreetingPresenceRecord greetingPresence) =>
        Sync(Required(_greetings, nameof(UpsertGreetingPresence)).UpsertAsync(greetingPresence));

    public IReadOnlyList<TrustedServerRecord> GetTrustedServers() =>
        Sync(Required(_trustedServers, nameof(GetTrustedServers)).ListAsync(includeInactive: true));
    public TrustedServerRecord? FindTrustedServer(string canonicalHost) => GetTrustedServers().FirstOrDefault(item =>
        item.CanonicalHost.Equals(NormalizeOperationalHost(canonicalHost), StringComparison.OrdinalIgnoreCase));
    public TrustedServerRecord UpsertTrustedServer(TrustedServerRecord trustedServer) =>
        Sync(Required(_trustedServers, nameof(UpsertTrustedServer)).UpsertAsync(trustedServer));

    public IReadOnlyList<TrustedServerAdmissionRecord> GetTrustedServerAdmissions(string? canonicalHost = null)
    {
        IReadOnlyList<TrustedServerRecord> servers = string.IsNullOrWhiteSpace(canonicalHost)
            ? GetTrustedServers()
            : FindTrustedServer(canonicalHost) is { } server ? [server] : [];
        return servers.SelectMany(server => Sync(Required(_trustedServers, nameof(GetTrustedServerAdmissions))
                .ListAdmissionsAsync(server.ServerId, 1000)))
            .OrderByDescending(item => item.CreatedUtc).ToArray();
    }

    public TrustedServerAdmissionRecord RecordTrustedServerAdmission(TrustedServerRecord trustedServer, string action,
        string? actorDeviceId, string? actorFriendlyId, string? reason = null)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = string.Join("|", trustedServer.ServerId, trustedServer.CanonicalHost, action, actorDeviceId,
            actorFriendlyId, reason, now.ToUnixTimeMilliseconds());
        var record = new TrustedServerAdmissionRecord
        {
            ServerId = trustedServer.ServerId, CanonicalHost = trustedServer.CanonicalHost,
            ServerKind = trustedServer.ServerKind, Action = string.IsNullOrWhiteSpace(action) ? "admit" : action.Trim(),
            ActorDeviceId = actorDeviceId?.Trim() ?? string.Empty,
            ActorFriendlyId = actorFriendlyId?.Trim() ?? string.Empty, Reason = reason?.Trim(),
            SignatureAlgorithm = "SHA256", SignatureKeyId = "openjibo-relational-admission-v1", Payload = payload,
            Signature = Sha256(payload), CreatedUtc = now
        };
        return Sync(Required(_trustedServers, nameof(RecordTrustedServerAdmission)).AddAdmissionAsync(record));
    }

    public void RevokeIdentityGraphAnchor(string anchor)
    {
        if (!string.IsNullOrWhiteSpace(anchor))
            Sync(Required(_trustedServers, nameof(RevokeIdentityGraphAnchor)).RevokeAnchorAsync(anchor.Trim(), null));
    }

    public IdentityGraphSnapshot GetIdentityGraph(string? loopId = null)
    {
        var account = GetAccount(); var robot = GetRobot(); var loop = OperationalLoop(loopId);
        var people = _people is null ? [] : Sync(_people.ListAsync(account.AccountId, loop, 1000));
        var members = _members is null ? [] : Sync(_members.ListAsync(account.AccountId, loop, 1000));
        var relationship = new IdentityGraphRelationship
        {
            SubjectId = account.AccountId, SubjectKind = "account", Relationship = "owns",
            ObjectId = loop, ObjectKind = "loop", LoopId = loop
        };
        var canonical = $"{account.AccountId}|{loop}|{robot.RobotId}|{robot.DeviceId}|{people.Count}|{members.Count}";
        var hash = Sha256(canonical);
        return new IdentityGraphSnapshot
        {
            AccountId = account.AccountId, LoopId = loop, RobotId = robot.RobotId, DeviceId = robot.DeviceId,
            ContentHash = hash, SignatureAlgorithm = "SHA256", SignatureKeyId = "openjibo-relational-identity-v1",
            SignaturePayload = canonical, Signature = hash, People = people, Members = members,
            Relationships = [relationship]
        };
    }

    private static T Required<T>(T? repository, string method) where T : class => repository ??
        throw new InvalidOperationException($"The normalized repository required by {method} is not configured.");
    private static string OperationalLoop(string? loopId) =>
        string.IsNullOrWhiteSpace(loopId) ? DefaultLoopId : loopId.Trim();
    private static string NormalizeOperationalHost(string host) => host.Trim().TrimEnd('.').ToLowerInvariant();
    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
