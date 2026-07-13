using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Holidays;

namespace Jibo.Cloud.Tests.Application;

public sealed class CountdownTargetResolverTests
{
    private readonly IHolidayCountdownCatalog _catalog = new HolidayCountdownCatalogLoader().LoadFromJson(
        """
        [
          {
            "canonicalName": "Christmas Day",
            "rule": { "type": "fixed", "month": 12, "day": 25 },
            "aliases": ["christmas", "christmas day"]
          }
        ]
        """);

    [Fact]
    public void TryResolve_HolidayAlias_ReturnsCanonicalName()
    {
        var resolver = new CountdownTargetResolver(_catalog);

        Assert.True(resolver.TryResolve("christmas", out var target));
        Assert.Equal("Christmas Day", target.Label);
        Assert.NotNull(target.Rule);
    }

    [Fact]
    public void TryResolve_Weekday_ReturnsWeekdayLabel()
    {
        var resolver = new CountdownTargetResolver(_catalog);

        Assert.True(resolver.TryResolve("monday", out var target));
        Assert.Equal("Monday", target.Label);
        Assert.Equal(DayOfWeek.Monday, target.Weekday);
    }

    [Fact]
    public void TryResolve_SpokenDate_ReturnsMonthDayLabel()
    {
        var resolver = new CountdownTargetResolver(_catalog);

        Assert.True(resolver.TryResolve("april ninth", out var target));
        Assert.Equal("April 9", target.Label);
        Assert.Equal(4, target.Month);
        Assert.Equal(9, target.Day);
    }

    [Fact]
    public void TryResolve_UnknownTarget_ReturnsFalse()
    {
        var resolver = new CountdownTargetResolver(_catalog);
        Assert.False(resolver.TryResolve("something unknown", out _));
    }
}
