using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Abstractions;

public interface IHolidayCalendarProvider
{
    IReadOnlyList<HolidayRecord> GetPublicHolidays(string? countryCode, int year);
}
