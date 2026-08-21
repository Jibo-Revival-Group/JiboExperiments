using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public interface ILoopTopologyRepository
{
    Task<IReadOnlyList<StoredLoopTopology>> ListForAccountAsync(string accountId, int limit = 100,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredLoopTopology>> ListForDeviceAsync(string accountId, string deviceId, int limit = 100,
        CancellationToken cancellationToken = default);
    Task<StoredLoopTopology?> GetAsync(string accountId, string loopId,
        CancellationToken cancellationToken = default);
    Task<StoredLoopTopology> UpsertAsync(StoredLoopTopology topology,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string accountId, string loopId, CancellationToken cancellationToken = default);
}

public sealed record StoredLoopTopology(LoopRecord Loop, IReadOnlyList<LoopDeviceLink> Devices);
public sealed record LoopDeviceLink(string DeviceId, bool IsPrimary, DateTimeOffset AddedUtc);

public interface ILoopMemberRepository
{
    Task<IReadOnlyList<LoopMemberRecord>> ListAsync(string accountId, string loopId, int limit = 250,
        CancellationToken cancellationToken = default);
    Task<LoopMemberRecord?> GetAsync(string accountId, string loopId, string memberId,
        CancellationToken cancellationToken = default);
    Task<LoopMemberRecord> UpsertAsync(string accountId, LoopMemberRecord member,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string accountId, string loopId, string memberId,
        CancellationToken cancellationToken = default);
}

public interface IPersonRepository
{
    Task<IReadOnlyList<PersonRecord>> ListAsync(string accountId, string loopId, int limit = 250,
        CancellationToken cancellationToken = default);
    Task<PersonRecord> UpsertAsync(PersonRecord person, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string accountId, string loopId, string personId,
        CancellationToken cancellationToken = default);
}

public interface IRecognitionObservationRepository
{
    Task<IReadOnlyList<RecognitionObservationRecord>> ListAsync(string accountId, string loopId, int limit = 250,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecognitionObservationRecord>> ListAllForBackupAsync(string accountId, string loopId,
        CancellationToken cancellationToken = default) => ListAsync(accountId, loopId, 1000, cancellationToken);
    Task<RecognitionObservationRecord> AddAsync(string accountId, RecognitionObservationRecord observation,
        CancellationToken cancellationToken = default);
}
