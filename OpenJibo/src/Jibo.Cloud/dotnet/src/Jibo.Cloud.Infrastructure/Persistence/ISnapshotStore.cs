namespace Jibo.Cloud.Infrastructure.Persistence;

public interface ISnapshotStore
{
    TSnapshot? Load<TSnapshot>() where TSnapshot : class;
    void Save<TSnapshot>(TSnapshot snapshot) where TSnapshot : class;
}
