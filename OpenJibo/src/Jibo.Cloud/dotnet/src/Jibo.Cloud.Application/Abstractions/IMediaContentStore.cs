namespace Jibo.Cloud.Application.Abstractions;

public interface IMediaContentStore
{
    Task StoreAsync(string path, string contentType, byte[] content, IReadOnlyDictionary<string, object?>? meta,
        CancellationToken cancellationToken = default);

    Task<MediaContentSnapshot?> LoadAsync(string path, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaContentItem>> ListAsync(string prefix, int maxCount = 100,
        CancellationToken cancellationToken = default);
}

public sealed record MediaContentSnapshot
{
    public string ContentType { get; init; } = "application/octet-stream";
    public byte[] Content { get; init; } = [];

    public IReadOnlyDictionary<string, object?> Meta { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}

public sealed record MediaContentItem
{
    public string Path { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public IReadOnlyDictionary<string, object?> Meta { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}
