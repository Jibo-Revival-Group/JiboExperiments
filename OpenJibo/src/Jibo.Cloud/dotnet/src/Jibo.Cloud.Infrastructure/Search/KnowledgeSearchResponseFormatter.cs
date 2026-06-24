using System.Text.RegularExpressions;

namespace Jibo.Cloud.Infrastructure.Search;

internal static partial class KnowledgeSearchResponseFormatter
{
    public static string NormalizeForSpeech(string answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText)) return string.Empty;

        var normalized = answerText.Trim();
        normalized = BoldPattern().Replace(normalized, "$1");
        normalized = WhitespacePattern().Replace(normalized, " ");
        return normalized.Trim();
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex BoldPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
