using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public interface ILoopKeyRepository
{
    Task<LoopSymmetricKeyRecord?> GetAsync(string accountId, string loopId, CancellationToken cancellationToken = default);
    Task<LoopSymmetricKeyRecord> UpsertAsync(string accountId, LoopSymmetricKeyRecord key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredKeyRequest>> ListRequestsAsync(string accountId, string loopId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredKeyRequest>> ListAllRequestsForBackupAsync(string accountId, string loopId,
        CancellationToken cancellationToken = default) => ListRequestsAsync(accountId, loopId, 1000, cancellationToken);
    Task<StoredKeyRequest> UpsertRequestAsync(string accountId, StoredKeyRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteRequestAsync(string accountId, string loopId, string requestId, CancellationToken cancellationToken = default);
}
public sealed record LoopSymmetricKeyRecord(string LoopId, byte[] EncryptedKey, string WrappingKeyId, string Algorithm, DateTimeOffset CreatedUtc, DateTimeOffset? RotatedUtc = null);
public sealed record StoredKeyRequest(KeyRequestRecord Request, string RequestKind = "incoming", string Status = "pending", DateTimeOffset? CompletedUtc = null);

public interface IHolidayOverrideRepository
{
    Task<IReadOnlyList<HolidayRecord>> ListAsync(string accountId, string loopId, int limit = 250, CancellationToken cancellationToken = default);
    Task<HolidayRecord> UpsertAsync(string accountId, HolidayRecord holiday, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string accountId, string loopId, string holidayId, CancellationToken cancellationToken = default);
}
public interface ICommuteProfileRepository
{
    Task<IReadOnlyList<CommuteProfileRecord>> ListAsync(string accountId, string loopId, int limit = 250, CancellationToken cancellationToken = default);
    Task<CommuteProfileRecord> UpsertAsync(string accountId, CommuteProfileRecord profile, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string accountId, string loopId, string profileId, CancellationToken cancellationToken = default);
}
public interface ICalendarEventRepository
{
    Task<IReadOnlyList<CalendarEventRecord>> ListAsync(string accountId, string loopId, DateOnly? from = null, DateOnly? to = null, int limit = 500, CancellationToken cancellationToken = default);
    Task<CalendarEventRecord> UpsertAsync(string accountId, CalendarEventRecord calendarEvent, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string accountId, string loopId, string eventId, CancellationToken cancellationToken = default);
}
public interface IGreetingPresenceRepository
{
    Task<IReadOnlyList<GreetingPresenceRecord>> ListAsync(string accountId, string loopId, int limit = 250, CancellationToken cancellationToken = default);
    Task<GreetingPresenceRecord> UpsertAsync(GreetingPresenceRecord presence, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string accountId, string loopId, string presenceId, CancellationToken cancellationToken = default);
}

public interface ITrustedServerRepository
{
    Task<IReadOnlyList<TrustedServerRecord>> ListAsync(bool includeInactive = false, int limit = 250, CancellationToken cancellationToken = default);
    Task<TrustedServerRecord> UpsertAsync(TrustedServerRecord server, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrustedServerAdmissionRecord>> ListAdmissionsAsync(string serverId, int limit = 250, CancellationToken cancellationToken = default);
    Task<TrustedServerAdmissionRecord> AddAdmissionAsync(TrustedServerAdmissionRecord admission, CancellationToken cancellationToken = default);
    Task<bool> RevokeAnchorAsync(string anchor, string? reason, CancellationToken cancellationToken = default);
    Task<bool> IsAnchorRevokedAsync(string anchor, CancellationToken cancellationToken = default);
}
