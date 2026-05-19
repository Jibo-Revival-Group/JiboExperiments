using System.Collections.Concurrent;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Holidays;

public sealed class NagerDateHolidayCalendarProvider : IHolidayCalendarProvider
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ConcurrentDictionary<string, HolidayRecord[]> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _countryCode;

    public NagerDateHolidayCalendarProvider()
        : this(new HolidayCalendarOptions())
    {
    }

    public NagerDateHolidayCalendarProvider(HolidayCalendarOptions options)
    {
        _countryCode = string.IsNullOrWhiteSpace(options.CountryCode) ? "US" : options.CountryCode.Trim();
    }

    public IReadOnlyList<HolidayRecord> GetPublicHolidays(string? countryCode, int year)
    {
        var resolvedCountryCode = string.IsNullOrWhiteSpace(countryCode) ? _countryCode : countryCode.Trim();
        var cacheKey = $"{resolvedCountryCode.ToUpperInvariant()}-{year}";
        return Cache.GetOrAdd(cacheKey, _ => LoadHolidays(resolvedCountryCode, year));
    }

    private static HolidayRecord[] LoadHolidays(string countryCode, int year)
    {
        try
        {
            var uri = $"https://date.nager.at/api/v3/publicholidays/{year}/{countryCode}";
            using var response = HttpClient.GetAsync(uri).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
            {
                using var stream = response.Content.ReadAsStream();
                var payload = JsonSerializer.Deserialize<NagerDateHolidayDto[]>(stream, JsonOptions) ?? [];
                var records = payload
                    .Where(item => item.Date.Year == year)
                    .Select(item => ToHolidayRecord(item, countryCode))
                    .OrderBy(record => record.Date)
                    .ThenBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (records.Length > 0) return records;
            }
        }
        catch
        {
            // Fall back to a small local holiday set so the robot still has something sensible to show.
        }

        return BuildFallbackHolidays(countryCode, year);
    }

    private static HolidayRecord ToHolidayRecord(NagerDateHolidayDto dto, string countryCode)
    {
        var eventId = $"{countryCode.ToUpperInvariant()}-{Slugify(dto.Name)}";
        return new HolidayRecord
        {
            Id = eventId,
            EventId = eventId,
            Name = dto.Name,
            Category = "holiday",
            LoopId = string.Empty,
            IsEnabled = true,
            Date = DateOnly.FromDateTime(dto.Date),
            Source = "nager-date",
            CountryCode = countryCode.ToUpperInvariant(),
            Created = DateTimeOffset.UtcNow
        };
    }

    private static HolidayRecord[] BuildFallbackHolidays(string countryCode, int year)
    {
        if (!countryCode.Equals("US", StringComparison.OrdinalIgnoreCase))
            return [];

        var easterSunday = CalculateEasterSunday(year);
        var holidays = new List<HolidayRecord>
        {
            FixedHoliday("New Year's Day", year, 1, 1, countryCode),
            ObservedHoliday("Martin Luther King Jr. Day", NthWeekdayOfMonth(year, 1, DayOfWeek.Monday, 3), countryCode),
            ObservedHoliday("Presidents Day", NthWeekdayOfMonth(year, 2, DayOfWeek.Monday, 3), countryCode),
            ObservedHoliday("Memorial Day", LastWeekdayOfMonth(year, 5, DayOfWeek.Monday), countryCode),
            FixedHoliday("Juneteenth", year, 6, 19, countryCode),
            FixedHoliday("Independence Day", year, 7, 4, countryCode),
            ObservedHoliday("Labor Day", NthWeekdayOfMonth(year, 9, DayOfWeek.Monday, 1), countryCode),
            ObservedHoliday("Thanksgiving", NthWeekdayOfMonth(year, 11, DayOfWeek.Thursday, 4), countryCode),
            FixedHoliday("Christmas", year, 12, 25, countryCode),
            ObservedHoliday("Easter", easterSunday, countryCode),
            ObservedHoliday("Good Friday", easterSunday.AddDays(-2), countryCode),
            ObservedHoliday("Palm Sunday", easterSunday.AddDays(-7), countryCode),
            ObservedHoliday("Ash Wednesday", easterSunday.AddDays(-46), countryCode),
            FixedHoliday("Halloween", year, 10, 31, countryCode),
            FixedHoliday("Valentine's Day", year, 2, 14, countryCode),
            ObservedHoliday("Mother's Day", NthWeekdayOfMonth(year, 5, DayOfWeek.Sunday, 2), countryCode),
            ObservedHoliday("Father's Day", NthWeekdayOfMonth(year, 6, DayOfWeek.Sunday, 3), countryCode)
        };

        return holidays
            .OrderBy(holiday => holiday.Date)
            .ThenBy(holiday => holiday.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HolidayRecord FixedHoliday(string name, int year, int month, int day, string countryCode)
    {
        return CreateHoliday(name, new DateOnly(year, month, day), countryCode);
    }

    private static HolidayRecord ObservedHoliday(string name, DateOnly date, string countryCode)
    {
        return CreateHoliday(name, date, countryCode);
    }

    private static HolidayRecord CreateHoliday(string name, DateOnly date, string countryCode)
    {
        var eventId = $"{countryCode.ToUpperInvariant()}-{Slugify(name)}";
        return new HolidayRecord
        {
            Id = eventId,
            EventId = eventId,
            Name = name,
            Category = "holiday",
            LoopId = string.Empty,
            IsEnabled = true,
            Date = date,
            Source = "fallback",
            CountryCode = countryCode.ToUpperInvariant(),
            Created = DateTimeOffset.UtcNow
        };
    }

    private static DateOnly NthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int occurrence)
    {
        var date = new DateOnly(year, month, 1);
        var offset = ((int)dayOfWeek - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(offset + 7 * (occurrence - 1));
    }

    private static DateOnly LastWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek)
    {
        var date = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)date.DayOfWeek - (int)dayOfWeek + 7) % 7;
        return date.AddDays(-offset);
    }

    private static DateOnly CalculateEasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    private static string Slugify(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasDash = false;
                continue;
            }

            if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private sealed class NagerDateHolidayDto
    {
        public DateTime Date { get; init; }
        public string LocalName { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string CountryCode { get; init; } = string.Empty;
        public bool Global { get; init; }
        public string[]? Counties { get; init; }
        public string[]? Types { get; init; }
    }
}
