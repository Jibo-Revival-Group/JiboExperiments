using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jibo.Cloud.Application.Services;

public sealed class RuntimePairingAttestationIssuerOptions
{
    public const string SectionName = "OpenJibo:ManagedPairingAttestation";
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string PrivateKeyPem { get; set; } = string.Empty;
    public int LifetimeSeconds { get; set; } = 120;
}

public sealed record RuntimePairingAttestationInput(
    Guid PairingId,
    Guid DeviceKeyId,
    int CredentialVersion,
    byte[] ChallengeHash,
    byte[] SessionBinding);

public interface IRuntimePairingAttestationIssuer
{
    bool IsReady { get; }
    string? TryIssue(RuntimePairingAttestationInput input);
}

public sealed class RejectingRuntimePairingAttestationIssuer : IRuntimePairingAttestationIssuer
{
    public static readonly RejectingRuntimePairingAttestationIssuer Instance = new();

    private RejectingRuntimePairingAttestationIssuer()
    {
    }

    public bool IsReady => false;
    public string? TryIssue(RuntimePairingAttestationInput input) => null;
}

/// <summary>
/// Produces the managed-service runtime half of the pairing ceremony from
/// already-trusted inputs. This signer does not discover robot identity,
/// establish co-presence, assign ownership, or transmit the assertion.
/// </summary>
public sealed class Es256RuntimePairingAttestationIssuer : IRuntimePairingAttestationIssuer, IDisposable
{
    public const string Purpose = "openjibo-managed-pairing";
    public const string Type = "openjibo-runtime-attestation+jwt";
    public const string SessionBindingDomain = "openjibo-runtime-pairing-session-binding-v1\0";
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(5);

    private readonly string _issuer;
    private readonly string _audience;
    private readonly string _keyId;
    private readonly TimeSpan _lifetime;
    private readonly ECDsa _key;
    private readonly TimeProvider _timeProvider;
    private readonly object _signingGate = new();

    private Es256RuntimePairingAttestationIssuer(
        RuntimePairingAttestationIssuerOptions options,
        ECDsa key,
        TimeProvider timeProvider)
    {
        _issuer = options.Issuer;
        _audience = options.Audience;
        _keyId = options.KeyId;
        _lifetime = TimeSpan.FromSeconds(options.LifetimeSeconds);
        _key = key;
        _timeProvider = timeProvider;
    }

    public bool IsReady => true;

    public static bool TryCreate(
        RuntimePairingAttestationIssuerOptions options,
        TimeProvider timeProvider,
        out IRuntimePairingAttestationIssuer issuer)
    {
        issuer = RejectingRuntimePairingAttestationIssuer.Instance;
        if (!options.Enabled ||
            !IsBoundedExactValue(options.Issuer, 200) ||
            !IsBoundedExactValue(options.Audience, 200) ||
            !IsBoundedIdentifier(options.KeyId) ||
            string.IsNullOrWhiteSpace(options.PrivateKeyPem) ||
            options.LifetimeSeconds is < 1 or > 300)
        {
            return false;
        }

        ECDsa? key = null;
        try
        {
            key = ECDsa.Create();
            key.ImportFromPem(options.PrivateKeyPem);
            var parameters = key.ExportParameters(includePrivateParameters: true);
            if (key.KeySize != 256 ||
                parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value ||
                parameters.D is not { Length: > 0 })
            {
                key.Dispose();
                return false;
            }

            issuer = new Es256RuntimePairingAttestationIssuer(options, key, timeProvider);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            key?.Dispose();
            return false;
        }
    }

    public string? TryIssue(RuntimePairingAttestationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PairingId == Guid.Empty || input.DeviceKeyId == Guid.Empty ||
            input.CredentialVersion < 1 || input.ChallengeHash is not { Length: 32 } ||
            input.SessionBinding is not { Length: 32 })
        {
            return null;
        }

        var issuedAt = _timeProvider.GetUtcNow();
        var expiresAt = issuedAt.Add(_lifetime);
        var jti = RandomNumberGenerator.GetBytes(32);
        var header = WriteHeader();
        var payload = WritePayload(input, issuedAt, expiresAt, jti);
        var encodedHeader = Encode(header);
        var encodedPayload = Encode(payload);
        var signingInput = Encoding.ASCII.GetBytes(encodedHeader + "." + encodedPayload);
        byte[] signature;
        lock (_signingGate)
        {
            signature = _key.SignData(
                signingInput,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        return encodedHeader + "." + encodedPayload + "." + Encode(signature);
    }

    public static byte[] CreateSessionBinding(string? rawHubToken)
    {
        if (string.IsNullOrWhiteSpace(rawHubToken)) return [];
        var tokenBytes = Encoding.UTF8.GetBytes(rawHubToken.Trim());
        var domainBytes = Encoding.UTF8.GetBytes(SessionBindingDomain);
        var material = new byte[domainBytes.Length + tokenBytes.Length];
        domainBytes.CopyTo(material, 0);
        tokenBytes.CopyTo(material, domainBytes.Length);
        return SHA256.HashData(material);
    }

    public void Dispose() => _key.Dispose();

    private byte[] WriteHeader()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("alg", "ES256");
            writer.WriteString("kid", _keyId);
            writer.WriteString("typ", Type);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private byte[] WritePayload(
        RuntimePairingAttestationInput input,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        byte[] jti)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteString("purpose", Purpose);
            writer.WriteString("iss", _issuer);
            writer.WriteString("aud", _audience);
            writer.WriteString("pairing_id", input.PairingId.ToString("D"));
            writer.WriteString("device_key_id", input.DeviceKeyId.ToString("D"));
            writer.WriteNumber("credential_version", input.CredentialVersion);
            writer.WriteString("challenge_hash", Encode(input.ChallengeHash));
            writer.WriteString("session_binding", Encode(input.SessionBinding));
            writer.WriteNumber("iat", issuedAt.ToUnixTimeSeconds());
            writer.WriteNumber("exp", expiresAt.ToUnixTimeSeconds());
            writer.WriteString("jti", Encode(jti));
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static bool IsBoundedExactValue(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool IsBoundedIdentifier(string value) =>
        value.Length is > 0 and <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static string Encode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
