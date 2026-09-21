using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Jibo.Cloud.Application.Services;

public sealed class AwsSigV4ReplayDigestKey
{
    private readonly byte[] _key;

    public AwsSigV4ReplayDigestKey(ReadOnlySpan<byte> key, short version = 1)
    {
        if (key.Length < 32)
            throw new ArgumentException("Replay digest key must contain at least 32 bytes.", nameof(key));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version));
        _key = key.ToArray();
        Version = version;
    }

    public short Version { get; }

    public static AwsSigV4ReplayDigestKey FromBase64Url(string encoded, short version = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoded);
        var normalized = encoded.Trim().Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        try
        {
            return new AwsSigV4ReplayDigestKey(Convert.FromBase64String(normalized), version);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Replay digest key must be base64url encoded.", nameof(encoded), exception);
        }
    }

    internal ReadOnlySpan<byte> Bytes => _key;
}

/// <summary>
/// Produces an opaque environment-keyed digest for a successfully authenticated
/// SigV4 credential proof. Raw request and credential material never crosses the
/// durable replay-store boundary.
/// </summary>
public static class AwsSigV4ReplayDigestFactory
{
    private const byte FormatVersion = 1;

    public static byte[] Create(
        AwsSigV4ReplayDigestKey key,
        string operation,
        string accessKeyId,
        string credentialDate,
        string region,
        string service,
        string signedTimestamp,
        ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (signature.Length != 32)
            throw new ArgumentException("SigV4 signature must contain exactly 32 bytes.", nameof(signature));

        using var transcript = new MemoryStream();
        transcript.WriteByte(FormatVersion);
        Append(transcript, operation, nameof(operation));
        Append(transcript, accessKeyId, nameof(accessKeyId));
        Append(transcript, credentialDate, nameof(credentialDate));
        Append(transcript, region, nameof(region));
        Append(transcript, service, nameof(service));
        Append(transcript, signedTimestamp, nameof(signedTimestamp));
        Append(transcript, signature);
        return HMACSHA256.HashData(key.Bytes, transcript.GetBuffer().AsSpan(0, checked((int)transcript.Length)));
    }

    private static void Append(Stream transcript, string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        Append(transcript, Encoding.UTF8.GetBytes(value));
    }

    private static void Append(Stream transcript, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        transcript.Write(length);
        transcript.Write(value);
    }
}
