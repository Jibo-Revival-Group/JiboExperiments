using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class HolidayDateCalculatorTests
{
    [Fact]
    public void GetNextOccurrence_FixedHoliday_RollsToNextYearWhenPast()
    {
        var rule = new HolidayDateRule { Type = "fixed", Month = 12, Day = 25 };
        var next = HolidayDateCalculator.GetNextOccurrence(new DateOnly(2026, 12, 26), rule);
        Assert.Equal(new DateOnly(2027, 12, 25), next);
    }

    [Fact]
    public void GetNextOccurrence_Thanksgiving2026_IsFourthThursdayInNovember()
    {
        var rule = new HolidayDateRule
        {
            Type = "nthWeekday",
            Month = 11,
            DayOfWeek = "Thursday",
            Occurrence = 4
        };

        var next = HolidayDateCalculator.GetNextOccurrence(new DateOnly(2026, 7, 12), rule);
        Assert.Equal(new DateOnly(2026, 11, 26), next);
    }

    [Fact]
    public void GetNextOccurrence_EasterOffset_ReturnsGoodFriday()
    {
        var rule = new HolidayDateRule { Type = "easterOffset", Days = -2 };
        var next = HolidayDateCalculator.GetNextOccurrence(new DateOnly(2026, 1, 1), rule);
        Assert.Equal(new DateOnly(2026, 4, 3), next);
    }

    [Fact]
    public void GetNextOccurrence_YearLookup_ReturnsNextHanukkah()
    {
        var rule = new HolidayDateRule
        {
            Type = "yearLookup",
            Dates = new Dictionary<string, string>
            {
                ["2026"] = "12-04",
                ["2027"] = "12-24"
            }
        };

        var next = HolidayDateCalculator.GetNextOccurrence(new DateOnly(2026, 12, 5), rule);
        Assert.Equal(new DateOnly(2027, 12, 24), next);
    }

    [Fact]
    public void GetNextWeekdayOccurrence_OnSameWeekday_ReturnsToday()
    {
        var next = HolidayDateCalculator.GetNextWeekdayOccurrence(new DateOnly(2026, 7, 13), DayOfWeek.Monday);
        Assert.Equal(new DateOnly(2026, 7, 13), next);
    }
}
