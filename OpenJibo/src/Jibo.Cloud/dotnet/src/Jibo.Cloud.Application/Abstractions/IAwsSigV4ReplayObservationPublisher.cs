using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Application.Abstractions;

/// <summary>
/// Best-effort, observe-only publication of a verified legacy credential proof.
/// Implementations must not alter request authorization or token issuance.
/// </summary>
public interface IAwsSigV4ReplayObservationPublisher
{
    bool TryPublish(AwsSigV4VerifiedProof proof, string operation);
}
public sealed class NullAwsSigV4ReplayObservationPublisher : IAwsSigV4ReplayObservationPublisher
{
    public static readonly NullAwsSigV4ReplayObservationPublisher Instance = new();

    private NullAwsSigV4ReplayObservationPublisher()
    {
    }

    public bool TryPublish(AwsSigV4VerifiedProof proof, string operation) => false;
}
