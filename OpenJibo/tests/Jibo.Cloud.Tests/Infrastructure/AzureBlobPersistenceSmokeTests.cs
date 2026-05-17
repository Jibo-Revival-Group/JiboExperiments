using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class AzureBlobPersistenceSmokeTests
{
    [Fact]
    public void AzureBlobSnapshotStore_RoundTripsState_WhenAzureBlobProfileIsConfigured()
    {
        var stateBackend = Environment.GetEnvironmentVariable("OpenJibo__State__Backend");
        var stateConnectionString = Environment.GetEnvironmentVariable("OpenJibo__State__ConnectionString");
        var personalMemoryBackend = Environment.GetEnvironmentVariable("OpenJibo__PersonalMemory__Backend");
        var personalMemoryConnectionString =
            Environment.GetEnvironmentVariable("OpenJibo__PersonalMemory__ConnectionString");

        if (!string.Equals(stateBackend, "AzureBlob", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(stateConnectionString))
            return;

        var factory = new PersistenceSnapshotStoreFactory();
        var snapshotName = $"smoke-{Guid.NewGuid():N}";
        var store = factory.Create(null, PersistenceBackendKind.AzureBlob, snapshotName, stateConnectionString);

        var payload = new SmokeSnapshot
        {
            Name = "azure-smoke",
            Value = "round-trip"
        };

        store.Save(payload);

        var loaded = store.Load<SmokeSnapshot>();

        Assert.NotNull(loaded);
        Assert.Equal(payload.Name, loaded!.Name);
        Assert.Equal(payload.Value, loaded.Value);
        Assert.Equal("AzureBlob", stateBackend);
        Assert.Equal("AzureBlob", personalMemoryBackend);
        Assert.False(string.IsNullOrWhiteSpace(personalMemoryConnectionString));
    }

    private sealed class SmokeSnapshot
    {
        public string Name { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }
}