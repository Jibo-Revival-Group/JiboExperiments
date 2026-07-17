using Jibo.Cloud.Infrastructure.Calendar;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class IcalUrlValidatorTests
{
    [Fact]
    public void TryValidateHttpsPublicUrl_RejectsHttpAndLoopback()
    {
        Assert.False(IcalUrlValidator.TryValidateHttpsPublicUrl(
            "http://calendar.google.com/calendar/ical/demo/basic.ics",
            out _,
            out var httpError));
        Assert.Contains("https", httpError, StringComparison.OrdinalIgnoreCase);

        Assert.False(IcalUrlValidator.TryValidateHttpsPublicUrl(
            "https://127.0.0.1/calendar.ics",
            out _,
            out var loopbackError));
        Assert.Contains("not allowed", loopbackError, StringComparison.OrdinalIgnoreCase);

        Assert.False(IcalUrlValidator.TryValidateHttpsPublicUrl(
            "https://localhost/calendar.ics",
            out _,
            out var localhostError));
        Assert.Contains("not allowed", localhostError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGetSafeHost_ReturnsHostOnly()
    {
        Assert.Equal(
            "calendar.google.com",
            IcalUrlValidator.TryGetSafeHost("https://calendar.google.com/calendar/ical/demo/private-token/basic.ics"));
    }

    [Fact]
    public void TryValidateHttpsPublicUrl_AllowsHostnameWithoutDnsOnSavePath()
    {
        Assert.True(IcalUrlValidator.TryValidateHttpsPublicUrl(
            "https://calendar.example.com/ical/private-token/basic.ics",
            out var uri,
            out var error,
            requireDnsResolution: false));
        Assert.Null(error);
        Assert.Equal("calendar.example.com", uri.Host);
    }
}
