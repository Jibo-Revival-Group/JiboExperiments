namespace Jibo.Cloud.Application.Abstractions;

public enum AwsSigV4ReplayObservationStatus
{
    FirstSeen,
    Repeat
}

public sealed record AwsSigV4ReplayObservation(
    AwsSigV4ReplayObservationStatus Status,
    long ObservationCount,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    DateTimeOffset ExpiresUtc);

/// <summary>
/// Stores only an environment-keyed digest of verified SigV4 replay material.
/// Implementations must atomically classify concurrent observations across replicas.
/// This boundary is observational and does not authorize or reject a request.
/// </summary>
public interface IAwsSigV4ReplayObservationStore
{
    Task<AwsSigV4ReplayObservation> ObserveAsync(
        ReadOnlyMemory<byte> replayDigest,
        short keyVersion,
        string operation,
        CancellationToken cancellationToken = default);
}
