using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class JiboVerificationServiceTests
{
    [Fact]
    public void ConfirmByCode_ReturnsInvalid_WhenCodeMissing()
    {
        var service = new JiboVerificationService();

        var result = service.TryConfirmByCode("DOES-NOT-EXIST");

        Assert.False(result.Ok);
        Assert.Equal("That verification code is invalid or has expired.", result.Error);
    }

    [Fact]
    public void ConfirmByCode_ReturnsToken_WhenCodeWasIssued()
    {
        var service = new JiboVerificationService();
        var issuedCode = service.IssueCodeForDevice("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020");

        var confirmed = service.TryConfirmByCode(issuedCode);

        Assert.True(confirmed.Ok);
        Assert.False(string.IsNullOrWhiteSpace(confirmed.Token));
        Assert.Equal("Ghost-Instance-Onion-Silk", confirmed.FriendlyId);

        var consumed = service.TryConsumeToken(confirmed.Token!);
        Assert.NotNull(consumed);
        Assert.Equal("Ghost-Instance-Onion-Silk", consumed.FriendlyId);
    }
}
