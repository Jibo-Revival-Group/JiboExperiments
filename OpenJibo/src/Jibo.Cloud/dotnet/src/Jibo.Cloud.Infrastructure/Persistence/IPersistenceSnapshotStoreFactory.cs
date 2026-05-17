namespace Jibo.Cloud.Infrastructure.Persistence;

public interface IPersistenceSnapshotStoreFactory
{
    ISnapshotStore Create(string? persistencePath, PersistenceBackendKind backendKind, string snapshotName,
        string? connectionString = null);
}