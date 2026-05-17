using System.Text.Json;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PersistenceSnapshotStoreFactory : IPersistenceSnapshotStoreFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ISnapshotStore Create(string? persistencePath, PersistenceBackendKind backendKind, string snapshotName, string? connectionString = null)
    {
        return backendKind switch
        {
            PersistenceBackendKind.File => new JsonFileSnapshotStore(persistencePath, JsonOptions),
            PersistenceBackendKind.AzureBlob => new AzureBlobSnapshotStore(
                connectionString ?? throw new InvalidOperationException("Azure Blob persistence requires a connection string."),
                snapshotName),
            PersistenceBackendKind.AzureSql => new AzureSqlSnapshotStore(
                connectionString ?? throw new InvalidOperationException("Azure SQL persistence requires a connection string."),
                snapshotName),
            _ => new JsonFileSnapshotStore(persistencePath, JsonOptions)
        };
    }
}
