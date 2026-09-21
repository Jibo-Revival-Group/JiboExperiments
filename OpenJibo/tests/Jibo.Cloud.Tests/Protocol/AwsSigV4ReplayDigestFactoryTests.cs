using System.Security.Cryptography;
using System.Text;
using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Protocol;

public sealed class AwsSigV4ReplayDigestFactoryTests
{
    private static readonly byte[] Signature = SHA256.HashData("signature-sentinel"u8);
    private static readonly AwsSigV4ReplayDigestKey Key = new(
        SHA256.HashData("environment-replay-key-sentinel"u8), 7);

    [Fact]
    public void Create_IsDeterministicOpaqueAndVersionedOutsideDigest()
    {
        var first = Create();
        var second = Create();

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.Equal(7, Key.Version);
        var encoded = Convert.ToHexString(first);
        Assert.DoesNotContain("credential-sentinel", encoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToHexString(Signature), encoded, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Account.CreateHubToken", "access-key-a", "20260920", "api", "jibo", "20260920T123456Z")]
    [InlineData("Notification.NewRobotToken", "access-key-a", "20260920", "api", "jibo", "20260920T123456Z")]
    [InlineData("Account.CreateHubToken", "access-key-b", "20260920", "api", "jibo", "20260920T123456Z")]
    [InlineData("Account.CreateHubToken", "access-key-a", "20260921", "api", "jibo", "20260920T123456Z")]
    [InlineData("Account.CreateHubToken", "access-key-a", "20260920", "us-east-1", "jibo", "20260920T123456Z")]
    [InlineData("Account.CreateHubToken", "access-key-a", "20260920", "api", "other", "20260920T123456Z")]
    [InlineData("Account.CreateHubToken", "access-key-a", "20260920", "api", "jibo", "20260920T123457Z")]
    public void Create_SeparatesEveryTranscriptField(
        string operation,
        string accessKeyId,
        string credentialDate,
        string region,
        string service,
        string signedTimestamp)
    {
        var baseline = Create();
        var changed = AwsSigV4ReplayDigestFactory.Create(
            Key, operation, accessKeyId, credentialDate, region, service, signedTimestamp, Signature);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void FromBase64Url_AcceptsFoundationSecretShapeAndRejectsShortKey()
    {
        var raw = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var parsed = AwsSigV4ReplayDigestKey.FromBase64Url(encoded, 3);

        Assert.Equal(3, parsed.Version);
        Assert.Throws<ArgumentException>(() =>
            AwsSigV4ReplayDigestKey.FromBase64Url(Convert.ToBase64String(new byte[16])));
    }

    private static byte[] Create() => AwsSigV4ReplayDigestFactory.Create(
        Key,
        "Account.CreateHubToken",
        "credential-sentinel",
        "20260920",
        "api",
        "jibo",
        "20260920T123456Z",
        Signature);
}
