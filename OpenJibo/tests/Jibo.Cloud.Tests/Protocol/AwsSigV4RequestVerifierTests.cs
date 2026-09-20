using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Tests.Protocol;

public sealed class AwsSigV4RequestVerifierTests
{
    private static readonly DateTimeOffset SignedAt =
        new(2026, 9, 20, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public void Verify_AcceptsPayloadAndTargetBoundRequest()
    {
        var store = new InMemoryCloudStateStore();
        var envelope = Sign(
            store.GetAccount(),
            "{}",
            SignedAt,
            ["host", "x-amz-content-sha256", "x-amz-date", "x-amz-target"]);

        var result = new AwsSigV4RequestVerifier(
            store,
            new FixedTimeProvider(SignedAt.AddMinutes(1))).Verify(envelope);

        Assert.Equal(AwsSigV4VerificationOutcome.VerifiedPayloadAndTarget, result.Outcome);
        Assert.True(result.CredentialAuthenticated);
        Assert.True(result.PayloadBound);
        Assert.True(result.TargetBound);
        Assert.NotNull(result.AccessKeyFingerprint);
    }

    [Fact]
    public void Verify_ClassifiesCapturedLegacyShapeAsCredentialOnly()
    {
        var store = new InMemoryCloudStateStore();
        var envelope = Sign(
            store.GetAccount(),
            "{\"deviceId\":\"Royal-Current-Sage-Canvas\"}",
            SignedAt,
            ["host", "x-amz-content-sha256", "x-amz-date"],
            advertisedPayloadHash: HexSha256([]));

        var result = new AwsSigV4RequestVerifier(
            store,
            new FixedTimeProvider(SignedAt)).Verify(envelope);

        Assert.Equal(AwsSigV4VerificationOutcome.VerifiedCredentialOnly, result.Outcome);
        Assert.True(result.CredentialAuthenticated);
        Assert.False(result.PayloadBound);
        Assert.False(result.TargetBound);
    }

    [Fact]
    public void Verify_RejectsChangedSignedHeader()
    {
        var store = new InMemoryCloudStateStore();
        var envelope = Sign(
            store.GetAccount(),
            "{}",
            SignedAt,
            ["host", "x-amz-content-sha256", "x-amz-date", "x-amz-target"]);
        envelope.Headers["X-Amz-Target"] = "Account_20151111.ResetKeys";

        var result = new AwsSigV4RequestVerifier(
            store,
            new FixedTimeProvider(SignedAt)).Verify(envelope);

        Assert.Equal(AwsSigV4VerificationOutcome.SignatureMismatch, result.Outcome);
        Assert.False(result.CredentialAuthenticated);
    }

    [Fact]
    public void Verify_RejectsExpiredRequestBeforeSignatureUse()
    {
        var store = new InMemoryCloudStateStore();
        var envelope = Sign(
            store.GetAccount(),
            "{}",
            SignedAt,
            ["host", "x-amz-content-sha256", "x-amz-date"]);

        var result = new AwsSigV4RequestVerifier(
            store,
            new FixedTimeProvider(SignedAt.AddMinutes(6))).Verify(envelope);

        Assert.Equal(AwsSigV4VerificationOutcome.Expired, result.Outcome);
        Assert.False(result.CredentialAuthenticated);
    }

    [Fact]
    public void Verify_RejectsUnknownCredentialWithoutLeakingIt()
    {
        var store = new InMemoryCloudStateStore();
        var unknown = new AccountProfile
        {
            AccessKeyId = "unknown-access-key",
            SecretAccessKey = "unknown-secret-key"
        };
        var envelope = Sign(
            unknown,
            "{}",
            SignedAt,
            ["host", "x-amz-content-sha256", "x-amz-date"]);

        var result = new AwsSigV4RequestVerifier(
            store,
            new FixedTimeProvider(SignedAt)).Verify(envelope);

        Assert.Equal(AwsSigV4VerificationOutcome.UnknownCredential, result.Outcome);
        Assert.DoesNotContain(unknown.AccessKeyId, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(unknown.SecretAccessKey, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_RejectsNonCanonicalSignedHeaderList()
    {
        var store = new InMemoryCloudStateStore();
        var envelope = Sign(
            store.GetAccount(),
            "{}",
            SignedAt,
            ["host", "x-amz-content-sha256", "x-amz-date"]);
        envelope.Headers["Authorization"] = envelope.Headers["Authorization"]
            .Replace("SignedHeaders=host;x-amz-content-sha256;x-amz-date",
                "SignedHeaders=x-amz-date;host;x-amz-content-sha256",
                StringComparison.Ordinal);

        var result = new AwsSigV4RequestVerifier(
            store,
            new FixedTimeProvider(SignedAt)).Verify(envelope);

        Assert.Equal(AwsSigV4VerificationOutcome.Malformed, result.Outcome);
    }

    [Fact]
    public void Verify_UsesWireHostHeaderInsteadOfHarnessHostNameOverride()
    {
        var store = new InMemoryCloudStateStore();
        var envelope = Sign(
            store.GetAccount(),
            "{}",
            SignedAt,
            ["host", "x-amz-content-sha256", "x-amz-date"]);
        envelope = new ProtocolEnvelope
        {
            Method = envelope.Method,
            HostName = "harness-override.invalid",
            Path = envelope.Path,
            Headers = envelope.Headers,
            BodyText = envelope.BodyText,
            BodyBytes = envelope.BodyBytes
        };

        var result = new AwsSigV4RequestVerifier(
            store,
            new FixedTimeProvider(SignedAt)).Verify(envelope);

        Assert.Equal(AwsSigV4VerificationOutcome.VerifiedCredentialOnly, result.Outcome);
    }

    [Fact]
    public void Verify_RejectsQueryBearingEnvelopeUntilRawQueryIsPreserved()
    {
        var store = new InMemoryCloudStateStore();
        var envelope = Sign(
            store.GetAccount(),
            "{}",
            SignedAt,
            ["host", "x-amz-content-sha256", "x-amz-date"]);
        envelope.QueryParameters["duplicate"] = "normalized";

        var result = new AwsSigV4RequestVerifier(
            store,
            new FixedTimeProvider(SignedAt)).Verify(envelope);

        Assert.Equal(AwsSigV4VerificationOutcome.Malformed, result.Outcome);
    }

    [Theory]
    [InlineData("GET", "/")]
    [InlineData("POST", "/v1/dispatch")]
    public void Verify_RejectsRequestsOutsideObservedRootPostContract(string method, string path)
    {
        var store = new InMemoryCloudStateStore();
        var signed = Sign(
            store.GetAccount(),
            "{}",
            SignedAt,
            ["host", "x-amz-content-sha256", "x-amz-date"]);
        var envelope = new ProtocolEnvelope
        {
            Method = method,
            HostName = signed.HostName,
            Path = path,
            Headers = signed.Headers,
            BodyText = signed.BodyText,
            BodyBytes = signed.BodyBytes
        };

        var result = new AwsSigV4RequestVerifier(
            store,
            new FixedTimeProvider(SignedAt)).Verify(envelope);

        Assert.Equal(AwsSigV4VerificationOutcome.Malformed, result.Outcome);
    }

    [Fact]
    public void HandleAccount_ObservesFailedSignatureWithoutRejectingOrLoggingSecrets()
    {
        var store = new InMemoryCloudStateStore();
        var unknown = new AccountProfile
        {
            AccessKeyId = "unknown-access-key",
            SecretAccessKey = "unknown-secret-key"
        };
        var envelope = Sign(
            unknown,
            "{}",
            SignedAt,
            ["host", "x-amz-content-sha256", "x-amz-date"]);
        var logger = new ListLogger<CloudAuthProtocolHandler>();
        var handler = new CloudAuthProtocolHandler(
            store,
            logger,
            awsSigV4Verifier: new AwsSigV4RequestVerifier(store, new FixedTimeProvider(SignedAt)));

        var response = handler.HandleAccount("CreateHubToken", envelope);
        var messages = string.Join('\n', logger.Messages);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains(nameof(AwsSigV4VerificationOutcome.UnknownCredential), messages, StringComparison.Ordinal);
        Assert.DoesNotContain(unknown.AccessKeyId, messages, StringComparison.Ordinal);
        Assert.DoesNotContain(unknown.SecretAccessKey, messages, StringComparison.Ordinal);
    }

    private static ProtocolEnvelope Sign(
        AccountProfile account,
        string body,
        DateTimeOffset signedAt,
        IReadOnlyList<string> signedHeaders,
        string? advertisedPayloadHash = null)
    {
        const string region = "us-east-1";
        const string service = "jibo";
        const string target = "Notification_20150505.NewRobotToken";
        var timestamp = signedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = signedAt.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var payloadHash = advertisedPayloadHash ?? HexSha256(Encoding.UTF8.GetBytes(body));
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Host"] = "api.jibo.com",
            ["X-Amz-Date"] = timestamp,
            ["X-Amz-Content-Sha256"] = payloadHash,
            ["X-Amz-Target"] = target
        };
        var canonicalHeaders = string.Join('\n', signedHeaders.Select(header =>
            $"{header}:{headers[header]}"));
        var signedHeadersText = string.Join(';', signedHeaders);
        var canonicalRequest = string.Join('\n',
            "POST",
            "/",
            string.Empty,
            canonicalHeaders + "\n",
            signedHeadersText,
            payloadHash);
        var scope = $"{date}/{region}/{service}/aws4_request";
        var stringToSign = string.Join('\n',
            "AWS4-HMAC-SHA256",
            timestamp,
            scope,
            HexSha256(Encoding.UTF8.GetBytes(canonicalRequest)));
        var signingKey = DeriveSigningKey(account.SecretAccessKey, date, region, service);
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            signingKey,
            Encoding.UTF8.GetBytes(stringToSign))).ToLowerInvariant();
        headers["Authorization"] =
            $"AWS4-HMAC-SHA256 Credential={account.AccessKeyId}/{scope}, " +
            $"SignedHeaders={signedHeadersText}, Signature={signature}";

        return new ProtocolEnvelope
        {
            Method = "POST",
            HostName = "api.jibo.com",
            Path = "/",
            Headers = headers,
            BodyText = body,
            BodyBytes = Encoding.UTF8.GetBytes(body)
        };
    }

    private static byte[] DeriveSigningKey(string secret, string date, string region, string service)
    {
        var dateKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes($"AWS4{secret}"), Encoding.UTF8.GetBytes(date));
        var regionKey = HMACSHA256.HashData(dateKey, Encoding.UTF8.GetBytes(region));
        var serviceKey = HMACSHA256.HashData(regionKey, Encoding.UTF8.GetBytes(service));
        return HMACSHA256.HashData(serviceKey, Encoding.UTF8.GetBytes("aws4_request"));
    }

    private static string HexSha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoOpScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));

        private sealed class NoOpScope : IDisposable
        {
            public static NoOpScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
