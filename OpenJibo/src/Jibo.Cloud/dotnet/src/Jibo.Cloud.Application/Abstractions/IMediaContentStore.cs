namespace Jibo.Cloud.Application.Abstractions;

public interface IMediaContentStore
{
    Task StoreAsync(string path, string contentType, byte[] content, IReadOnlyDictionary<string, object?>? meta,
        CancellationToken cancellationToken = default);

    Task<MediaContentSnapshot?> LoadAsync(string path, CancellationToken cancellationToken = default);
}

public sealed record MediaContentSnapshot
{
    public string ContentType { get; init; } = "application/octet-stream";
    public byte[] Content { get; init; } = [];

    public IReadOnlyDictionary<string, object?> Meta { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}