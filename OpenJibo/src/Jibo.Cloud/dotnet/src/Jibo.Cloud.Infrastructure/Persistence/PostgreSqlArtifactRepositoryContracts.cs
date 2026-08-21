using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public interface IUpdateManifestRepository
{
    Task<IReadOnlyList<StoredUpdateManifest>> ListAsync(string? subsystem = null, string? filter = null,
        CancellationToken cancellationToken = default);
    Task<StoredUpdateManifest?> GetAsync(string updateId, CancellationToken cancellationToken = default);
    Task<StoredUpdateManifest> UpsertAsync(StoredUpdateManifest update,
        CancellationToken cancellationToken = default);
    Task<StoredUpdateManifest?> DeleteAsync(string updateId, CancellationToken cancellationToken = default);
}

public sealed record StoredUpdateManifest(
    UpdateManifest Manifest,
    IReadOnlyDictionary<string, object?> Dependencies);

public interface IMediaMetadataRepository
{
    Task<IReadOnlyList<StoredMediaRecord>> ListAsync(string accountId, IReadOnlyList<string>? loopIds = null,
        DateTimeOffset? after = null, DateTimeOffset? before = null, int limit = 250,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredMediaRecord>> GetAsync(string accountId, IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredMediaRecord>> ListAllForBackupAsync(string accountId, string loopId,
        CancellationToken cancellationToken = default) => ListAsync(accountId, [loopId], limit: 1000,
        cancellationToken: cancellationToken);
    Task<StoredMediaRecord> UpsertAsync(StoredMediaRecord media, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredMediaRecord>> SoftDeleteAsync(string accountId, IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);
}

public sealed record StoredMediaRecord(
    MediaRecord Media,
    string? ContentSha256 = null,
    long? ContentLength = null,
    string? EncryptionKeyId = null,
    DateTimeOffset? DeletedUtc = null);

public interface IBackupManifestRepository
{
    Task<IReadOnlyList<BackupManifestRecord>> ListAsync(string accountId, string? loopId = null,
        CancellationToken cancellationToken = default);
    Task<BackupManifestRecord?> GetAsync(string accountId, string backupId,
        CancellationToken cancellationToken = default);
    Task<BackupManifestRecord> UpsertAsync(BackupManifestRecord backup,
        CancellationToken cancellationToken = default);
    Task<BackupManifestRecord?> MarkRestoredAsync(string accountId, string backupId,
        DateTimeOffset restoredUtc, CancellationToken cancellationToken = default);
}

public sealed record BackupManifestRecord(
    string BackupId,
    string? AccountId,
    string? LoopId,
    string Name,
    string BlobUri,
    string ContentSha256,
    long ContentLength,
    int BackupSchemaVersion,
    string Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ExpiresUtc = null,
    DateTimeOffset? RestoredUtc = null);
