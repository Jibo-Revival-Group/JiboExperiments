using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal sealed class AzureBlobSnapshotStore : ISnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly BlobContainerClient _containerClient;
    private readonly string _blobName;

    public AzureBlobSnapshotStore(string connectionString, string snapshotName, string containerName = "openjibo-snapshots")
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Azure Blob persistence requires a storage connection string.");
        }

        if (string.IsNullOrWhiteSpace(snapshotName))
        {
            throw new ArgumentException("A snapshot name is required for Azure Blob persistence.", nameof(snapshotName));
        }

        _containerClient = new BlobContainerClient(connectionString, string.IsNullOrWhiteSpace(containerName) ? "openjibo-snapshots" : containerName);
        _blobName = $"{snapshotName}.json";
    }

    public TSnapshot? Load<TSnapshot>() where TSnapshot : class
    {
        try
        {
            if (!_containerClient.Exists())
            {
                return default;
            }

            var blobClient = _containerClient.GetBlobClient(_blobName);
            if (!blobClient.Exists())
            {
                return default;
            }

            var content = blobClient.DownloadContent();
            var json = content.Value.Content.ToString();
            return string.IsNullOrWhiteSpace(json)
                ? default
                : JsonSerializer.Deserialize<TSnapshot>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    public void Save<TSnapshot>(TSnapshot snapshot) where TSnapshot : class
    {
        _containerClient.CreateIfNotExists(PublicAccessType.None);
        var blobClient = _containerClient.GetBlobClient(_blobName);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        blobClient.Upload(BinaryData.FromString(json), overwrite: true);
    }
}
