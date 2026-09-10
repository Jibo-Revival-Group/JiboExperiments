using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Media;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class FileMediaContentStoreTests
{
    [Fact]
    public async Task EnumerateAsync_ReturnsEveryManifestBeyondListLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-media-enumeration-{Guid.NewGuid():N}");
        try
        {
            var store = new FileMediaContentStore(root);
            for (var index = 0; index < 1_005; index++)
            {
                await store.StoreAsync($"logs/{index}.txt", "text/plain", [(byte)index],
                    new Dictionary<string, object?> { ["deviceId"] = "source-device" });
            }

            var items = new List<MediaContentItem>();
            await foreach (var item in store.EnumerateAsync("logs")) items.Add(item);

            Assert.Equal(1_005, items.Count);
            Assert.Equal(1_000, (await store.ListAsync("logs", 1_000)).Count);
            Assert.Contains(items, item => item.Path.Equals("logs/1004.txt", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnumerateAsync_HonorsCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-media-enumeration-{Guid.NewGuid():N}");
        try
        {
            var store = new FileMediaContentStore(root);
            await store.StoreAsync("logs/item.txt", "text/plain", [1], null);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in store.EnumerateAsync("logs", cancellation.Token)) { }
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
