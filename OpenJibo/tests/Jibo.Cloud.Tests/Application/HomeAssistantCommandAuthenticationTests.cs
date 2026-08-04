using System.Globalization;
using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class HomeAssistantCommandAuthenticationTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void SignAndVerify_RoundTrips()
    {
        var fields = BuildFields();
        var signature = HomeAssistantCommandAuthentication.Sign(fields, Secret);
        fields["signature"] = signature;

        Assert.True(HomeAssistantCommandAuthentication.Verify(fields, Secret, signature));
    }

    [Fact]
    public void Verify_RejectsTamperedField()
    {
        var fields = BuildFields();
        var signature = HomeAssistantCommandAuthentication.Sign(fields, Secret);
        fields["targetName"] = "tampered";
        fields["signature"] = signature;

        Assert.False(HomeAssistantCommandAuthentication.Verify(fields, Secret, signature));
    }

    [Fact]
    public void Verify_RejectsWrongSecret()
    {
        var fields = BuildFields();
        var signature = HomeAssistantCommandAuthentication.Sign(fields, Secret);
        fields["signature"] = signature;

        Assert.False(HomeAssistantCommandAuthentication.Verify(fields, "ff" + Secret[2..], signature));
    }

    [Fact]
    public void Verify_RejectsSkewedTimestamp()
    {
        var now = DateTimeOffset.Parse("2026-08-01T15:00:00Z", CultureInfo.InvariantCulture);
        var fields = BuildFields(now.AddSeconds(-120));
        var signature = HomeAssistantCommandAuthentication.Sign(fields, Secret);
        fields["signature"] = signature;

        Assert.False(HomeAssistantCommandAuthentication.Verify(fields, Secret, signature, now));
    }

    [Fact]
    public void BuildCanonical_SortsKeysAndOmitsSignature()
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = "command",
            ["command"] = "lights_on_named",
            ["targetName"] = "kitchen",
            ["signature"] = "should-be-omitted",
            ["linkId"] = "abc"
        };

        var canonical = HomeAssistantCommandAuthentication.BuildCanonical(fields);

        Assert.Equal(
            "command=lights_on_named\nlinkId=abc\ntargetName=kitchen\ntype=command",
            canonical);
    }

    [Fact]
    public void GenerateCommandSecret_Returns64HexChars()
    {
        var secret = HomeAssistantCommandAuthentication.GenerateCommandSecret();
        Assert.Equal(64, secret.Length);
        Assert.Matches("^[0-9a-f]{64}$", secret);
    }

    private static Dictionary<string, string> BuildFields(DateTimeOffset? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = "command",
            ["command"] = "lights_off_named",
            ["targetName"] = "zanes",
            ["linkId"] = "link-1",
            ["timestamp"] = ts.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["nonce"] = "aabbccddeeff00112233445566778899"
        };
    }
}
