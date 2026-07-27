namespace Jibo.Cloud.Application.Services;

internal static class LegacyMimTemplateRenderer
{
    internal static string Render(string template, string? displayName, string? referent = null)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;

        var speaker = displayName?.Trim() ?? string.Empty;
        var resolvedReferent = referent?.Trim();
        if (string.IsNullOrWhiteSpace(resolvedReferent)) resolvedReferent = speaker;

        var rendered = template
            .Replace("${loopMember}", speaker, StringComparison.OrdinalIgnoreCase)
            .Replace("${speaker}", speaker, StringComparison.OrdinalIgnoreCase)
            .Replace("${referent}", resolvedReferent, StringComparison.OrdinalIgnoreCase);

        rendered = rendered
            .Replace(" ,", ",", StringComparison.Ordinal)
            .Replace(",,", ",", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Replace(" .", ".", StringComparison.Ordinal)
            .Replace(" !", "!", StringComparison.Ordinal)
            .Replace(" ?", "?", StringComparison.Ordinal);

        return rendered.Trim();
    }
}
