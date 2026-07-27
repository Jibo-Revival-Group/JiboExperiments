namespace Jibo.Cloud.Application.Services;

internal static class LegacyMimConditionEvaluator
{
    internal readonly record struct Context(
        string? HolidayClaim,
        string? Holiday,
        DateOnly CurrentDate,
        string? PodClaim = null,
        string? Pod = null,
        bool HasSpeaker = false,
        string? Emotion = null);

    internal static bool Matches(string? condition, Context context)
    {
        var normalized = Normalize(condition);
        if (string.IsNullOrWhiteSpace(normalized)) return true;

        return SplitTopLevel(normalized, "||")
            .Any(clause => MatchesAndClause(clause, context));
    }

    private static bool MatchesAndClause(string clause, Context context)
    {
        foreach (var part in SplitTopLevel(clause, "&&"))
        {
            if (!MatchesAtomic(part.Trim(), context)) return false;
        }

        return true;
    }

    private static bool MatchesAtomic(string atom, Context context)
    {
        if (string.IsNullOrWhiteSpace(atom)) return true;

        if (atom.StartsWith('(') && atom.EndsWith(')'))
            return Matches(atom[1..^1].Trim(), context);

        if (string.Equals(atom, "loopMember", StringComparison.OrdinalIgnoreCase))
            return context.HasSpeaker;

        if (string.Equals(atom, "!speaker", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(atom, "!loopMember", StringComparison.OrdinalIgnoreCase))
            return !context.HasSpeaker;

        if (string.Equals(atom, "!!speaker", StringComparison.OrdinalIgnoreCase))
            return context.HasSpeaker;

        if (string.Equals(atom, "!jibo.emotion", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(context.Emotion);

        if (TryMatchEquality(atom, "holidayclaim", out var holidayClaimExpected))
            return StringEquals(holidayClaimExpected, context.HolidayClaim);

        if (TryMatchEquality(atom, "holiday", out var holidayExpected))
        {
            var holiday = context.Holiday ?? context.HolidayClaim;
            return StringEquals(holidayExpected, holiday);
        }

        if (TryMatchEquality(atom, "podclaim", out var podClaimExpected))
            return StringEquals(podClaimExpected, context.PodClaim);

        if (TryMatchEquality(atom, "pod", out var podExpected))
        {
            var pod = context.PodClaim ?? context.Pod;
            return StringEquals(podExpected, pod);
        }

        if (TryMatchEquality(atom, "jibo.emotion", out var emotionExpected))
        {
            var actual = string.IsNullOrWhiteSpace(context.Emotion) ? "NEUTRAL" : context.Emotion;
            return StringEquals(emotionExpected, actual);
        }

        if (atom.Contains("isinrange(", StringComparison.OrdinalIgnoreCase))
            return JiboLegacyDateRange.MatchesDateRangeCondition(atom, context.CurrentDate);

        return false;
    }

    private static bool TryMatchEquality(string atom, string key, out string expected)
    {
        expected = string.Empty;
        foreach (var op in new[] { "===", "==" })
        {
            var prefix = key + op;
            if (!atom.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            expected = Unquote(atom[prefix.Length..]);
            return true;
        }

        return false;
    }

    private static IEnumerable<string> SplitTopLevel(string text, string delimiter)
    {
        var depth = 0;
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
            }

            if (depth != 0 || index + delimiter.Length > text.Length) continue;

            if (!text.AsSpan(index, delimiter.Length).Equals(delimiter, StringComparison.Ordinal)) continue;

            yield return text[start..index].Trim();
            index += delimiter.Length - 1;
            start = index + 1;
        }

        yield return text[start..].Trim();
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) ||
             (trimmed.StartsWith('\'') && trimmed.EndsWith('\''))))
            return trimmed[1..^1];

        return trimmed;
    }

    private static bool StringEquals(string left, string? right)
    {
        return !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? condition) =>
        string.IsNullOrWhiteSpace(condition)
            ? string.Empty
            : condition.Trim()
                .Replace(" && ", "&&", StringComparison.Ordinal)
                .Replace(" || ", "||", StringComparison.Ordinal);
}
