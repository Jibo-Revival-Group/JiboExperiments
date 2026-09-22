using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Xunit;

namespace Jibo.Cloud.Tests.Application;

public sealed class RuntimePairingAttestationIssuerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-21T20:00:00Z");

    [Fact]
    public void TryCreate_FailsClosedWhenDisabledOrMisconfigured()
    {
        Assert.False(Es256RuntimePairingAttestationIssuer.TryCreate(
            new RuntimePairingAttestationIssuerOptions(),
            new FixedTimeProvider(Now),
            out var disabled));
        Assert.False(disabled.IsReady);

        Assert.False(Es256RuntimePairingAttestationIssuer.TryCreate(
            new RuntimePairingAttestationIssuerOptions
            {
                Enabled = true,
                Issuer = "runtime.openjibo.test",
                Audience = "managed.openjibo.test",
                KeyId = "runtime-key-1",
                PrivateKeyPem = "not-a-key"
            },
            new FixedTimeProvider(Now),
            out var invalid));
        Assert.False(invalid.IsReady);

        using var privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicOnly = CreateOptions(privateKey);
        publicOnly.PrivateKeyPem = privateKey.ExportSubjectPublicKeyInfoPem();
        Assert.False(Es256RuntimePairingAttestationIssuer.TryCreate(
            publicOnly,
            new FixedTimeProvider(Now),
            out var missingPrivateKey));
        Assert.False(missingPrivateKey.IsReady);

        var excessiveLifetime = CreateOptions(privateKey);
        excessiveLifetime.LifetimeSeconds = 301;
        Assert.False(Es256RuntimePairingAttestationIssuer.TryCreate(
            excessiveLifetime,
            new FixedTimeProvider(Now),
            out var excessive));
        Assert.False(excessive.IsReady);
    }

    [Fact]
    public void TryIssue_ProducesExactManagedContractAndValidEs256Signature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = CreateOptions(key);
        Assert.True(Es256RuntimePairingAttestationIssuer.TryCreate(
            options,
            new FixedTimeProvider(Now),
            out var issuer));
        using var disposable = Assert.IsAssignableFrom<IDisposable>(issuer);

        var input = new RuntimePairingAttestationInput(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            7,
            SHA256.HashData("challenge"u8),
            Es256RuntimePairingAttestationIssuer.CreateSessionBinding("opaque-hub-token"));

        var compact = Assert.IsType<string>(issuer.TryIssue(input));
        var segments = compact.Split('.');
        Assert.Equal(3, segments.Length);

        using var header = JsonDocument.Parse(Decode(segments[0]));
        AssertExactNames(header.RootElement, "alg", "kid", "typ");
        Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal(options.KeyId, header.RootElement.GetProperty("kid").GetString());
        Assert.Equal(Es256RuntimePairingAttestationIssuer.Type,
            header.RootElement.GetProperty("typ").GetString());

        using var payload = JsonDocument.Parse(Decode(segments[1]));
        AssertExactNames(payload.RootElement,
            "version", "purpose", "iss", "aud", "pairing_id", "device_key_id",
            "credential_version", "challenge_hash", "session_binding", "iat", "exp", "jti");
        Assert.Equal(1, payload.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(Es256RuntimePairingAttestationIssuer.Purpose,
            payload.RootElement.GetProperty("purpose").GetString());
        Assert.Equal(options.Issuer, payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal(options.Audience, payload.RootElement.GetProperty("aud").GetString());
        Assert.Equal(input.PairingId.ToString("D"), payload.RootElement.GetProperty("pairing_id").GetString());
        Assert.Equal(input.DeviceKeyId.ToString("D"),
            payload.RootElement.GetProperty("device_key_id").GetString());
        Assert.Equal(input.CredentialVersion,
            payload.RootElement.GetProperty("credential_version").GetInt32());
        Assert.Equal(input.ChallengeHash,
            Decode(payload.RootElement.GetProperty("challenge_hash").GetString()!));
        Assert.Equal(input.SessionBinding,
            Decode(payload.RootElement.GetProperty("session_binding").GetString()!));
        Assert.Equal(Now.ToUnixTimeSeconds(), payload.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(Now.AddMinutes(2).ToUnixTimeSeconds(), payload.RootElement.GetProperty("exp").GetInt64());
        Assert.Equal(32, Decode(payload.RootElement.GetProperty("jti").GetString()!).Length);

        var signedBytes = Encoding.ASCII.GetBytes(segments[0] + "." + segments[1]);
        Assert.True(key.VerifyData(
            signedBytes,
            Decode(segments[2]),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void TryIssue_RejectsIncompleteTrustedInputs()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.True(Es256RuntimePairingAttestationIssuer.TryCreate(
            CreateOptions(key),
            new FixedTimeProvider(Now),
            out var issuer));
        using var disposable = Assert.IsAssignableFrom<IDisposable>(issuer);

        Assert.Null(issuer.TryIssue(new RuntimePairingAttestationInput(
            Guid.Empty,
            Guid.NewGuid(),
            1,
            new byte[32],
            new byte[32])));
        Assert.Null(issuer.TryIssue(new RuntimePairingAttestationInput(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            new byte[31],
            new byte[32])));
    }

    [Fact]
    public void SessionBinding_IsDomainSeparatedAndDoesNotExposeToken()
    {
        var binding = Es256RuntimePairingAttestationIssuer.CreateSessionBinding("opaque-hub-token");
        var other = Es256RuntimePairingAttestationIssuer.CreateSessionBinding("other-token");
        var unseparated = SHA256.HashData(Encoding.UTF8.GetBytes("opaque-hub-token"));

        Assert.Equal(32, binding.Length);
        Assert.NotEqual(binding, other);
        Assert.NotEqual(binding, unseparated);
        Assert.Empty(Es256RuntimePairingAttestationIssuer.CreateSessionBinding(" "));
    }

    private static RuntimePairingAttestationIssuerOptions CreateOptions(ECDsa key) => new()
    {
        Enabled = true,
        Issuer = "runtime.openjibo.test",
        Audience = "managed.openjibo.test",
        KeyId = "runtime-key-1",
        PrivateKeyPem = key.ExportPkcs8PrivateKeyPem(),
        LifetimeSeconds = 120
    };

    private static void AssertExactNames(JsonElement value, params string[] expected) =>
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

    private static byte[] Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
