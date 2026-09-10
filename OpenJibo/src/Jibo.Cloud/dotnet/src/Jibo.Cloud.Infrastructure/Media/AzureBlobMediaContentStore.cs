using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Media;

public sealed class AzureBlobMediaContentStore : IMediaContentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly BlobContainerClient _containerClient;
    private readonly ConcurrentDictionary<string, ManifestCacheEntry> _manifestCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _listingGate = new(1, 1);
    private const int MaxCachedManifests = 4096;

    public AzureBlobMediaContentStore(BlobContainerClient containerClient)
    {
        _containerClient = containerClient ?? throw new ArgumentNullException(nameof(containerClient));
    }

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

    public async IAsyncEnumerable<MediaContentItem> EnumerateAsync(string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : MediaPathHelper.GetRelativeStoragePath(prefix).Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(normalizedPrefix)) normalizedPrefix += "/";

        await _listingGate.WaitAsync(cancellationToken);
        try
        {
            await using var enumerator = _containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None,
                normalizedPrefix, cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                BlobItem blob;
                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                    blob = enumerator.Current;
                }
                catch (RequestFailedException exception) when (exception.Status == 404)
                {
                    yield break;
                }

                if (!blob.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                var item = await ReadManifestAsync(blob, cancellationToken);
                if (item is not null) yield return item;
            }
        }
        finally
        {
            _listingGate.Release();
        }
    }

    public async Task<IReadOnlyList<MediaContentItem>> ListAsync(string prefix, int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : MediaPathHelper.GetRelativeStoragePath(prefix).Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(normalizedPrefix)) normalizedPrefix += "/";
        // Blob listing includes ETags. Reuse unchanged manifests, while still
        // enumerating the full prefix so maxCount means newest, not lexical first.
        await _listingGate.WaitAsync(cancellationToken);
        try
        {
            var manifests = new List<BlobItem>();
            try
            {
                await foreach (var blob in _containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None,
                                   normalizedPrefix, cancellationToken))
                {
                    if (blob.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        manifests.Add(blob);
                }
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                _manifestCache.Clear();
                return [];
            }

            var present = manifests.Select(blob => blob.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var key in _manifestCache.Keys)
                if (key.StartsWith(normalizedPrefix, StringComparison.Ordinal) && !present.Contains(key))
                    _manifestCache.TryRemove(key, out _);

            var items = new ConcurrentBag<MediaContentItem>();
            await Parallel.ForEachAsync(manifests,
                new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
                async (blob, token) =>
                {
                    var etag = blob.Properties.ETag;
                    if (etag.HasValue && _manifestCache.TryGetValue(blob.Name, out var cached) && cached.ETag == etag)
                    {
                        items.Add(cached.Item);
                        return;
                    }
                    _manifestCache.TryRemove(blob.Name, out _);
                    try
                    {
                        var content = await _containerClient.GetBlobClient(blob.Name).DownloadContentAsync(token);
                        using var document = JsonDocument.Parse(content.Value.Content.ToStream());
                        var root = document.RootElement;
                        var path = root.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
                        if (string.IsNullOrWhiteSpace(path)) return;
                        var contentType = root.TryGetProperty("contentType", out var typeElement)
                            ? typeElement.GetString() ?? "application/octet-stream"
                            : "application/octet-stream";
                        var meta = root.TryGetProperty("meta", out var metaElement) && metaElement.ValueKind == JsonValueKind.Object
                            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(metaElement.GetRawText(), JsonOptions) ?? []
                            : new Dictionary<string, object?>();
                        var item = new MediaContentItem { Path = path, ContentType = contentType, Meta = meta };
                        items.Add(item);
                        if (etag.HasValue && _manifestCache.Count < MaxCachedManifests)
                            _manifestCache[blob.Name] = new ManifestCacheEntry(etag.Value, item);
                    }
                    catch (JsonException)
                    {
                        // One malformed historical manifest must not break the listing.
                    }
                    catch (RequestFailedException exception) when (exception.Status == 404)
                    {
                        // A retention job may delete a manifest after enumeration.
                    }
                });

            return items.OrderByDescending(item => ReadStoredUtc(item.Meta))
                .ThenBy(item => item.Path, StringComparer.Ordinal)
                .Take(Math.Max(1, maxCount)).ToArray();
        }
        finally
        {
            _listingGate.Release();
        }
    }

    private sealed record ManifestCacheEntry(ETag ETag, MediaContentItem Item);

    private async Task<MediaContentItem?> ReadManifestAsync(BlobItem blob, CancellationToken cancellationToken)
    {
        var etag = blob.Properties.ETag;
        if (etag.HasValue && _manifestCache.TryGetValue(blob.Name, out var cached) && cached.ETag == etag)
            return cached.Item;

        _manifestCache.TryRemove(blob.Name, out _);
        try
        {
            var content = await _containerClient.GetBlobClient(blob.Name).DownloadContentAsync(cancellationToken);
            using var document = JsonDocument.Parse(content.Value.Content.ToStream());
            var root = document.RootElement;
            var path = root.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(path)) return null;
            var contentType = root.TryGetProperty("contentType", out var typeElement)
                ? typeElement.GetString() ?? "application/octet-stream"
                : "application/octet-stream";
            var meta = root.TryGetProperty("meta", out var metaElement) && metaElement.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(metaElement.GetRawText(), JsonOptions) ?? []
                : new Dictionary<string, object?>();
            var item = new MediaContentItem { Path = path, ContentType = contentType, Meta = meta };
            if (etag.HasValue && _manifestCache.Count < MaxCachedManifests)
                _manifestCache[blob.Name] = new ManifestCacheEntry(etag.Value, item);
            return item;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    private static DateTimeOffset ReadStoredUtc(IReadOnlyDictionary<string, object?> meta) =>
        meta.TryGetValue("storedUtc", out var value) &&
        DateTimeOffset.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}
