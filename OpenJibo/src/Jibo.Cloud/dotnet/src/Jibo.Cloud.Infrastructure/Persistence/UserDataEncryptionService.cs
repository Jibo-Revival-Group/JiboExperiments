using System.Security.Cryptography;
using System.Text;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class UserDataEncryptionService
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int Pbkdf2Iterations = 100_000;

    private readonly byte[] _key;

    public UserDataEncryptionService()
        : this(
            Environment.GetEnvironmentVariable("OPENJIBO_USER_ENCRYPT"),
            Environment.GetEnvironmentVariable("OPENJIBO_USER_SALT"))
    {
    }

    internal UserDataEncryptionService(string? passphrase, string? salt)
    {
        _key = DeriveKey(passphrase ?? string.Empty, salt ?? string.Empty);
    }

    public EncryptedPayload Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var combined = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);

        return new EncryptedPayload(1, Convert.ToBase64String(nonce), Convert.ToBase64String(combined));
    }

    public string Decrypt(EncryptedPayload payload)
    {
        var nonce = Convert.FromBase64String(payload.Nonce);
        var combined = Convert.FromBase64String(payload.Ciphertext);
        if (combined.Length < 16)
            throw new CryptographicException("Ciphertext is too short.");

        var ciphertextLength = combined.Length - 16;
        var ciphertext = combined[..ciphertextLength];
        var tag = combined[ciphertextLength..];
        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DeriveKey(string passphrase, string salt)
    {
        var saltBytes = Encoding.UTF8.GetBytes(salt);
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            saltBytes,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySizeBytes);
    }

    public sealed record EncryptedPayload(int Version, string Nonce, string Ciphertext);
}
