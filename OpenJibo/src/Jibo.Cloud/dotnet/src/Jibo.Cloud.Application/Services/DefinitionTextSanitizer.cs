using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static class DefinitionTextSanitizer
{
    private static readonly Regex LeadingParentheticalPattern = new(
        @"^\([^)]+\)\s*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Sanitize(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition)) return string.Empty;

        var sanitized = definition.Trim();
        while (LeadingParentheticalPattern.IsMatch(sanitized))
            sanitized = LeadingParentheticalPattern.Replace(sanitized, string.Empty, 1).Trim();

        return sanitized;
    }
}
