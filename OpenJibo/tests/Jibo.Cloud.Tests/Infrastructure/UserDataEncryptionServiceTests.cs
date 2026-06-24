using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class UserDataEncryptionServiceTests
{
    [Fact]
    public void EncryptDecrypt_RoundTripsPlaintext()
    {
        var service = new UserDataEncryptionService("test-passphrase", "test-salt");
        const string plaintext = """{"schemaVersion":1,"homeAssistantLinks":[]}""";

        var encrypted = service.Encrypt(plaintext);
        var decrypted = service.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_ThrowsWhenSaltChanges()
    {
        var original = new UserDataEncryptionService("test-passphrase", "test-salt");
        var changed = new UserDataEncryptionService("test-passphrase", "other-salt");
        var encrypted = original.Encrypt("secret");

        Assert.ThrowsAny<Exception>(() => changed.Decrypt(encrypted));
    }

    [Fact]
    public void EncryptedStore_LoadOrReset_ResetsWhenEncryptedDataIsInvalid()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openjibo-user-data-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not valid encrypted json");

        try
        {
            var store = new EncryptedUserDataSnapshotStore(
                path,
                new UserDataEncryptionService("test-passphrase", "test-salt"));

            var snapshot = store.LoadOrReset();

            Assert.Empty(snapshot.HomeAssistantLinks);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}