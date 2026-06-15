using System.Security.Cryptography;
using System.Text.Json;
using Azure.Storage.Blobs;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Media;

internal sealed class AzureBlobMediaContentStore : IMediaContentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly BlobContainerClient _containerClient;

    public AzureBlobMediaContentStore(string? connectionString, string containerName = "openjibo-media")
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Azure Blob media persistence requires a storage connection string.");

        _containerClient = new BlobContainerClient(connectionString,
            string.IsNullOrWhiteSpace(containerName) ? "openjibo-media" : containerName);
    }

    public async Task StoreAsync(string path, string contentType, byte[] content,
        IReadOnlyDictionary<string, object?>? meta, CancellationToken cancellationToken = default)
    {
        var relative = MediaPathHelper.GetRelativeStoragePath(path);
        var contentBlob = _containerClient.GetBlobClient($"{relative}.bin");
        var metaBlob = _containerClient.GetBlobClient($"{relative}.json");
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await contentBlob.UploadAsync(new MemoryStream(content), true, cancellationToken);
        var manifestMeta = meta is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(meta, StringComparer.OrdinalIgnoreCase);
        manifestMeta["contentLength"] = content.Length;
        manifestMeta["contentSha256"] = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        manifestMeta["storedUtc"] = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            path,
            contentType,
            meta = manifestMeta
        }, JsonOptions);
        await metaBlob.UploadAsync(BinaryData.FromString(payload), true, cancellationToken);
    }

    public async Task<MediaContentSnapshot?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var relative = MediaPathHelper.GetRelativeStoragePath(path);
        var contentBlob = _containerClient.GetBlobClient($"{relative}.bin");
        if (!await contentBlob.ExistsAsync(cancellationToken)) return null;

        var content = await contentBlob.DownloadContentAsync(cancellationToken);
        var contentType = "application/octet-stream";
        IDictionary<string, object?> meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var metaBlob = _containerClient.GetBlobClient($"{relative}.json");

        if (!await metaBlob.ExistsAsync(cancellationToken))
            return new MediaContentSnapshot
            {
                ContentType = contentType,
                Content = content.Value.Content.ToArray(),
                Meta = meta as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>(meta)
            };

        try
        {
            var json = (await metaBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("contentType", out var type) && type.ValueKind == JsonValueKind.String)
                contentType = type.GetString() ?? contentType;

            if (root.TryGetProperty("meta", out var metaElement) && metaElement.ValueKind == JsonValueKind.Object)
                meta = JsonSerializer.Deserialize<Dictionary<string, object?>>(metaElement.GetRawText(),
                           JsonOptions) ??
                       new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Keep the raw binary available even if metadata parsing fails.
        }

        return new MediaContentSnapshot
        {
            ContentType = contentType,
            Content = content.Value.Content.ToArray(),
            Meta = meta as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>(meta)
        };
    }
}