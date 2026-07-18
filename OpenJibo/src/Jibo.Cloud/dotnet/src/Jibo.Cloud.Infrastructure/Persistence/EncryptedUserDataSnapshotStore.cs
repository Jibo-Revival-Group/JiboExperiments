using System.Text.Json;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class EncryptedUserDataSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly UserDataEncryptionService _encryptionService;
    private readonly ILogger<EncryptedUserDataSnapshotStore> _logger;

    private readonly string? _persistencePath;

    public EncryptedUserDataSnapshotStore(
        string? persistencePath,
        UserDataEncryptionService encryptionService,
        ILogger<EncryptedUserDataSnapshotStore>? logger = null)
    {
        _persistencePath = persistencePath;
        _encryptionService = encryptionService;
        _logger = logger ?? NullLogger<EncryptedUserDataSnapshotStore>.Instance;
    }

    public UserIntegrationSnapshot LoadOrReset()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
            return CreateEmptySnapshot();

        try
        {
            var envelope = JsonSerializer.Deserialize<UserDataEncryptedEnvelope>(
                File.ReadAllText(_persistencePath),
                JsonOptions);
            if (envelope is null)
                return ResetAndSave("User integration file was empty.");

            var plaintext = _encryptionService.Decrypt(new UserDataEncryptionService.EncryptedPayload(
                envelope.Version,
                envelope.Nonce,
                envelope.Ciphertext));

            var snapshot = JsonSerializer.Deserialize<UserIntegrationSnapshot>(plaintext, JsonOptions);
            if (snapshot is null ||
                snapshot.SchemaVersion < UserIntegrationSnapshot.MinimumSupportedSchemaVersion ||
                snapshot.SchemaVersion > UserIntegrationSnapshot.CurrentSchemaVersion)
                return ResetAndSave("User integration schema is unsupported.");

            // v1 snapshots omit MemberCalendarFeeds; normalize to current schema on load.
            if (snapshot.SchemaVersion < UserIntegrationSnapshot.CurrentSchemaVersion)
                return new UserIntegrationSnapshot
                {
                    SchemaVersion = UserIntegrationSnapshot.CurrentSchemaVersion,
                    HomeAssistantLinks = snapshot.HomeAssistantLinks ?? [],
                    MemberCalendarFeeds = snapshot.MemberCalendarFeeds ?? []
                };

            return snapshot;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to decrypt or parse user integration data at {Path}. Resetting to empty state.",
                _persistencePath);
            return ResetAndSave("User integration data could not be decrypted.");
        }
    }

    public void Save(UserIntegrationSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(_persistencePath)) return;

        var directory = Path.GetDirectoryName(_persistencePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var plaintext = JsonSerializer.Serialize(snapshot, JsonOptions);
        var encrypted = _encryptionService.Encrypt(plaintext);
        var envelope = new UserDataEncryptedEnvelope
        {
            Version = encrypted.Version,
            Nonce = encrypted.Nonce,
            Ciphertext = encrypted.Ciphertext
        };

        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory,
            $".{Path.GetFileName(_persistencePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(envelope, JsonOptions));

            if (File.Exists(_persistencePath))
                File.Replace(tempPath, _persistencePath, null);
            else
                File.Move(tempPath, _persistencePath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private UserIntegrationSnapshot ResetAndSave(string reason)
    {
        _logger.LogWarning("Resetting user integration store: {Reason}", reason);
        var snapshot = CreateEmptySnapshot();
        Save(snapshot);
        return snapshot;
    }

    private static UserIntegrationSnapshot CreateEmptySnapshot()
    {
        return new UserIntegrationSnapshot
        {
            SchemaVersion = UserIntegrationSnapshot.CurrentSchemaVersion,
            HomeAssistantLinks = [],
            MemberCalendarFeeds = []
        };
    }

    private sealed class UserDataEncryptedEnvelope
    {
        public int Version { get; init; } = 1;
        public string Nonce { get; init; } = string.Empty;
        public string Ciphertext { get; init; } = string.Empty;
    }
}