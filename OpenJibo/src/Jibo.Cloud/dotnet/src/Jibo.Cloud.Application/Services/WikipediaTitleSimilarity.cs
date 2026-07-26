using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static partial class WikipediaTitleSimilarity
{
    private static readonly HashSet<string> LeadingArticles = new(StringComparer.Ordinal)
    {
        "a",
        "an",
        "the"
    };

    public static bool IsCloseMatch(string? query, string? title)
    {
        var queryTokens = Tokenize(query);
        var titleTokens = Tokenize(title);
        if (queryTokens.Count == 0 || titleTokens.Count == 0) return false;

        if (TokensEqual(queryTokens, titleTokens)) return true;

        if (IsContiguousSubsequence(queryTokens, titleTokens) ||
            IsContiguousSubsequence(titleTokens, queryTokens))
            return true;

        if (queryTokens.Count == 1)
            return titleTokens.Contains(queryTokens[0], StringComparer.Ordinal);

        var intersection = queryTokens.Intersect(titleTokens, StringComparer.Ordinal).Count();
        var union = queryTokens.Union(titleTokens, StringComparer.Ordinal).Count();
        return union > 0 && (double)intersection / union >= 0.75;
    }

    internal static IReadOnlyList<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var withoutParenthetical = ParentheticalSuffixPattern().Replace(value, " ");
        var builder = new StringBuilder(withoutParenthetical.Length);
        foreach (var character in withoutParenthetical.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            builder.Append(' ');
        }

        var tokens = builder
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1 || char.IsDigit(token[0]))
            .Where(token => !LeadingArticles.Contains(token))
            .ToArray();

        return tokens;
    }

    private static bool TokensEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count) return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool IsContiguousSubsequence(
        IReadOnlyList<string> shorter,
        IReadOnlyList<string> longer)
    {
        if (shorter.Count == 0 || shorter.Count > longer.Count) return false;

        for (var start = 0; start <= longer.Count - shorter.Count; start++)
        {
            var matched = true;
            for (var offset = 0; offset < shorter.Count; offset++)
            {
                if (!string.Equals(shorter[offset], longer[start + offset], StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }
            }

            if (matched) return true;
        }

        return false;
    }

    [GeneratedRegex(@"\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalSuffixPattern();
}
