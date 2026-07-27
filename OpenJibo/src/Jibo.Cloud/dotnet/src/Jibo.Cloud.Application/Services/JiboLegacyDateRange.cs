using System.Globalization;

namespace Jibo.Cloud.Application.Services;

internal static class JiboLegacyDateRange
{
    internal static bool IsDateInRange(DateOnly currentDate, int startMonth, int startDay, int endMonth, int endDay)
    {
        var currentValue = currentDate.Month * 100 + currentDate.Day;
        var startValue = startMonth * 100 + startDay;
        var endValue = endMonth * 100 + endDay;

        return startValue <= endValue
            ? currentValue >= startValue && currentValue <= endValue
            : currentValue >= startValue || currentValue <= endValue;
    }

    internal static bool TryParseMonthDay(string value, out int month, out int day)
    {
        month = 0;
        day = 0;

        var trimmed = value.Trim().Trim('\'', '"');
        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out month)) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out day)) return false;

        return month is >= 1 and <= 12 && day is >= 1 and <= 31;
    }

    internal static bool MatchesDateRangeCondition(string clause, DateOnly currentDate)
    {
        var normalizedClause = clause.Trim().ToLowerInvariant();
        normalizedClause = normalizedClause.Replace("_now.", "dt.now.", StringComparison.Ordinal);

        if (!normalizedClause.StartsWith("dt.now.isinrange(", StringComparison.Ordinal)) return false;

        var openParenIndex = normalizedClause.IndexOf('(');
        var closeParenIndex = normalizedClause.LastIndexOf(')');
        if (openParenIndex < 0 || closeParenIndex <= openParenIndex) return false;

        var arguments = normalizedClause[(openParenIndex + 1)..closeParenIndex];
        var parts = arguments.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        if (!TryParseMonthDay(parts[0], out var startMonth, out var startDay)) return false;
        if (!TryParseMonthDay(parts[1], out var endMonth, out var endDay)) return false;

        return IsDateInRange(currentDate, startMonth, startDay, endMonth, endDay);
    }
}
