using System.Net;
using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static class LegacyMimPromptNormalizer
{
    private static readonly Regex PlaceholderPattern = new(
        @"\$\{[^}]+\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SsaEmotionPattern = new(
        @"<ssa\s+cat\s*=\s*['""](?<cat>[^'""]+)['""][^>]*/?>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnimBlockPattern = new(
        @"<anim[^>]*>(.*?)</anim>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AnimSelfClosingPattern = new(
        @"<anim[^>]*/>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StyleBlockPattern = new(
        @"<style[^>]*>(.*?)</style>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex PitchBlockPattern = new(
        @"<pitch[^>]*>(.*?)</pitch>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SsaStripPattern = new(
        @"<ssa[^>]*/?>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SpaceBeforePunctuationPattern = new(
        @"\s+([,.;:!?])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public sealed class Result
    {
        public string Text { get; init; } = string.Empty;
        public string? Emotion { get; init; }
    }

    private static readonly Regex ResidualMarkupPattern = new(
        @"<[^>]+>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MarkupOrTextPattern = new(
        @"<[^>]+>|[^<]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static string FormatTextOutsideMarkup(string markup, Func<string, string> format) =>
        MarkupOrTextPattern.Replace(markup, match => match.Value[0] == '<' ? match.Value : format(match.Value));

    public static Result Normalize(string? prompt, bool preservePlaceholders, bool preserveTtsMarkup = false,
        bool preserveAnimation = false, bool preservePitch = false)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return new Result();

        var text = WebUtility.HtmlDecode(prompt);
        string? emotion = null;

        foreach (Match match in SsaEmotionPattern.Matches(text))
            emotion = match.Groups["cat"].Value.Trim();

        if (!preserveTtsMarkup || !preservePitch)
            text = PitchBlockPattern.Replace(text, match => $" {match.Groups[1].Value} ");
        text = StyleBlockPattern.Replace(text, match => $" {match.Groups[1].Value} ");
        if (!preserveTtsMarkup || !preserveAnimation)
        {
            text = AnimSelfClosingPattern.Replace(text, " ");
            text = AnimBlockPattern.Replace(text, match => $" {match.Groups[1].Value} ");
        }
        text = SsaStripPattern.Replace(text, " ");

        if (!preservePlaceholders) text = PlaceholderPattern.Replace(text, " ");
        if (!preserveTtsMarkup) text = ResidualMarkupPattern.Replace(text, " ");

        text = (preserveTtsMarkup ? FormatTextOutsideMarkup(text, NormalizeSpacing) : NormalizeSpacing(text)).Trim();
        text = text.TrimStart('.', ',', ';', ':', '!', '?', ' ');

        return new Result
        {
            Text = text.Trim(),
            Emotion = string.IsNullOrWhiteSpace(emotion) ? null : emotion
        };
    }

    private static string NormalizeSpacing(string text) =>
        SpaceBeforePunctuationPattern.Replace(WhitespacePattern.Replace(text, " "), "$1");
}
