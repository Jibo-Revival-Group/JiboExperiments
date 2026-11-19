using System.Security;
using System.Xml;
using System.Xml.Linq;

namespace Jibo.Cloud.Application.Services;

internal static class LegacyMimTemplateRenderer
{
    internal static string Render(string template, string? displayName, string? referent = null)
    {
        // ReplyText is plain text. Keep template tags out of it before inserting names,
        // so literal markup characters in a person's name are not treated as template tags.
        return RenderTemplate(
            LegacyMimPromptNormalizer.Normalize(template, preservePlaceholders: true).Text,
            displayName, referent);
    }

    internal static string? RenderEsml(string template, string? displayName, string? referent = null,
        bool preserveAnimation = false, bool preservePitch = false)
    {
        var markup = LegacyMimPromptNormalizer.Normalize(template, preservePlaceholders: true,
            preserveTtsMarkup: true, preserveAnimation: preserveAnimation, preservePitch: preservePitch).Text;
        if (!markup.Contains('<')) return null;

        // Only catalog template markup is executable. Escape both literal template
        // ampersands and substituted values exactly once.
        markup = RenderTemplate(markup.Replace("&", "&amp;", StringComparison.Ordinal),
            SecurityElement.Escape(displayName), SecurityElement.Escape(referent));
        var esml = $"<speak><es cat='neutral' filter='!ssa-only, !sfx-only' endNeutral='true'>{markup}</es></speak>";
        try
        {
            var root = XElement.Parse(esml);
            if (root.Descendants().Any(element =>
                    element.Name.Namespace != XNamespace.None ||
                    (element.Name.LocalName is not ("es" or "break" or "phoneme" or "duration") &&
                     !(preserveAnimation && element.Name.LocalName == "anim") &&
                     !(preservePitch && element.Name.LocalName == "pitch"))))
                return null;
        }
        catch (XmlException)
        {
            // An invalid legacy fragment must not be sent as literal speech markup.
            return null;
        }

        return esml;
    }

    private static string RenderTemplate(string template, string? displayName, string? referent)
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
