using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Jibo.Cloud.Infrastructure.Media;
using Moq;
using System.Text.Json;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class AzureBlobMediaContentStoreTests
{
    [Fact]
    public async Task Listing_ReusesUnchangedManifestsAndRefreshesChangedAndDeletedEntries()
    {
        var container = new Mock<BlobContainerClient>();
        var manifests = new List<BlobItem>();
        container.Setup(client => client.GetBlobsAsync(BlobTraits.None, BlobStates.None,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => AsyncPageable<BlobItem>.FromPages([
                Page<BlobItem>.FromValues(manifests.ToArray(), null, Mock.Of<Response>())]));
        var oldBlob = AddManifest("logs/a.json", "old", "2026-09-01T00:00:00Z");
        var newBlob = AddManifest("logs/z.json", "new", "2026-09-08T00:00:00Z");
        var store = new AzureBlobMediaContentStore(container.Object);

        Assert.Equal("new", Assert.Single(await store.ListAsync("", 1)).Path);
        Assert.Equal("new", Assert.Single(await store.ListAsync("", 1)).Path);
        oldBlob.Verify(client => client.DownloadContentAsync(It.IsAny<CancellationToken>()), Times.Once);
        newBlob.Verify(client => client.DownloadContentAsync(It.IsAny<CancellationToken>()), Times.Once);

        manifests[0] = Item("logs/a.json", "v2");
        oldBlob.Setup(client => client.DownloadContentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Download("updated", "2026-09-09T00:00:00Z"));
        Assert.Equal("updated", Assert.Single(await store.ListAsync("", 1)).Path);
        oldBlob.Verify(client => client.DownloadContentAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        manifests.RemoveAt(0);
        Assert.Equal("new", Assert.Single(await store.ListAsync("", 10)).Path);

        Mock<BlobClient> AddManifest(string name, string path, string storedUtc)
        {
            manifests.Add(Item(name, "v1"));
            var blob = new Mock<BlobClient>();
            blob.Setup(client => client.DownloadContentAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Download(path, storedUtc));
            container.Setup(client => client.GetBlobClient(name)).Returns(blob.Object);
            return blob;
        }
    }

    [Fact]
    public async Task Listing_ReadsManifestsConcurrentlyWithBoundedFanout()
    {
        var container = new Mock<BlobContainerClient>();
        var manifests = Enumerable.Range(0, 20).Select(i => Item($"logs/{i}.json", "v1")).ToArray();
        container.Setup(client => client.GetBlobsAsync(BlobTraits.None, BlobStates.None,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncPageable<BlobItem>.FromPages([
                Page<BlobItem>.FromValues(manifests, null, Mock.Of<Response>())]));
        var running = 0;
        var peak = 0;
        var firstWave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var item in manifests)
        {
            var blob = new Mock<BlobClient>();
            blob.Setup(client => client.DownloadContentAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken token) =>
                {
                    var current = Interlocked.Increment(ref running);
                    int previous;
                    do { previous = peak; }
                    while (current > previous && Interlocked.CompareExchange(ref peak, current, previous) != previous);
                    if (current == 8) firstWave.TrySetResult();
                    await firstWave.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                    Interlocked.Decrement(ref running);
                    return Download(item.Name, "2026-09-08T00:00:00Z");
                });
            container.Setup(client => client.GetBlobClient(item.Name)).Returns(blob.Object);
        }
        var store = new AzureBlobMediaContentStore(container.Object);
        Assert.Equal(20, (await store.ListAsync("", 20)).Count);
        Assert.Equal(8, peak);
    }

    private static BlobItem Item(string name, string etag) => BlobsModelFactory.BlobItem(
        name: name, properties: BlobsModelFactory.BlobItemProperties(accessTierInferred: false, eTag: new ETag(etag)));

    private static Response<BlobDownloadResult> Download(string path, string storedUtc) => Response.FromValue(
        BlobsModelFactory.BlobDownloadResult(BinaryData.FromString(JsonSerializer.Serialize(new
        {
            path, contentType = "text/plain", meta = new { storedUtc }
        }))), Mock.Of<Response>());
}
