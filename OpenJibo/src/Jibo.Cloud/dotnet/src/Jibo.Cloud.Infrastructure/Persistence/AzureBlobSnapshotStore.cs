using System.Text.Json;
using Azure.Storage.Blobs;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal sealed class AzureBlobSnapshotStore : ISnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _blobName;

    private readonly BlobContainerClient _containerClient;

    public AzureBlobSnapshotStore(string connectionString, string snapshotName,
        string containerName = "openjibo-snapshots")
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Azure Blob persistence requires a storage connection string.");

        if (string.IsNullOrWhiteSpace(snapshotName))
            throw new ArgumentException("A snapshot name is required for Azure Blob persistence.",
                nameof(snapshotName));

        _containerClient = new BlobContainerClient(connectionString,
            string.IsNullOrWhiteSpace(containerName) ? "openjibo-snapshots" : containerName);
        _blobName = $"{snapshotName}.json";
    }

    public TSnapshot? Load<TSnapshot>() where TSnapshot : class
    {
        try
        {
            if (!_containerClient.Exists()) return null;

            var blobClient = _containerClient.GetBlobClient(_blobName);
            if (!blobClient.Exists()) return null;

            var content = blobClient.DownloadContent();
            var json = content.Value.Content.ToString();
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<TSnapshot>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save<TSnapshot>(TSnapshot snapshot) where TSnapshot : class
    {
        _containerClient.CreateIfNotExists();
        var blobClient = _containerClient.GetBlobClient(_blobName);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        blobClient.Upload(BinaryData.FromString(json), true);
    }
}