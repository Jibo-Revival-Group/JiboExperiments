using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static partial class RobotLaunchRuleParser
{
    [GeneratedRegex(
        @"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<pattern>\(.+\))\s*;\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex RuleDefinitionPattern();

    [GeneratedRegex(@"\{%\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*'(?<value>[^']*)'\s*%\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex SingleQuotedEntityPattern();

    [GeneratedRegex(@"\{%\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*""(?<value>[^""]*)""\s*%\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex DoubleQuotedEntityPattern();

    [GeneratedRegex(@"\{%\s*(?<key>skill|intent|domain)\s*:\s*'(?<value>[^']*)'\s*%\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColonQuotedEntityPattern();

    [GeneratedRegex(@"\{\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*'(?<value>[^']*)'\s*\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex BraceSingleQuotedEntityPattern();

    [GeneratedRegex(@"\{\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*""(?<value>[^""]*)""\s*\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex BraceDoubleQuotedEntityPattern();

    public static IReadOnlyList<ParsedLaunchRule> Parse(string fileName, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];

        var rules = new List<ParsedLaunchRule>();
        foreach (Match match in RuleDefinitionPattern().Matches(content))
        {
            var ruleName = match.Groups["name"].Value;
            var pattern = match.Groups["pattern"].Value;
            var entities = ExtractEntities(pattern);
            var literalTokens = ExtractLiteralTokens(pattern);

            if (literalTokens.Count == 0 && !entities.ContainsKey("skill")) continue;

            rules.Add(new ParsedLaunchRule
            {
                RuleName = ruleName,
                SourceFile = fileName,
                LiteralTokens = literalTokens,
                Entities = entities
            });
        }

        return rules;
    }

    private static IReadOnlyDictionary<string, string> ExtractEntities(string pattern)
    {
        var entities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in SingleQuotedEntityPattern().Matches(pattern))
            entities[match.Groups["key"].Value] = NormalizeEntityValue(match.Groups["value"].Value);

        foreach (Match match in DoubleQuotedEntityPattern().Matches(pattern))
            entities[match.Groups["key"].Value] = NormalizeEntityValue(match.Groups["value"].Value);

        foreach (Match match in ColonQuotedEntityPattern().Matches(pattern))
            entities[match.Groups["key"].Value] = NormalizeEntityValue(match.Groups["value"].Value);

        foreach (Match match in BraceSingleQuotedEntityPattern().Matches(pattern))
            entities[match.Groups["key"].Value] = NormalizeEntityValue(match.Groups["value"].Value);

        foreach (Match match in BraceDoubleQuotedEntityPattern().Matches(pattern))
            entities[match.Groups["key"].Value] = NormalizeEntityValue(match.Groups["value"].Value);

        return entities;
    }

    private static string NormalizeEntityValue(string value)
    {
        return value.Trim().TrimStart('\\');
    }

    private static string StripEntityMarkers(string pattern)
    {
        var withoutEntities = SingleQuotedEntityPattern().Replace(pattern, " ");
        withoutEntities = DoubleQuotedEntityPattern().Replace(withoutEntities, " ");
        withoutEntities = ColonQuotedEntityPattern().Replace(withoutEntities, " ");
        withoutEntities = BraceSingleQuotedEntityPattern().Replace(withoutEntities, " ");
        return BraceDoubleQuotedEntityPattern().Replace(withoutEntities, " ");
    }

    private static IReadOnlyList<string> ExtractLiteralTokens(string pattern)
    {
        var withoutEntities = StripEntityMarkers(pattern);
        withoutEntities = withoutEntities
            .Replace("$*", " ", StringComparison.Ordinal)
            .Replace("$+", " ", StringComparison.Ordinal)
            .Replace("$?", " ", StringComparison.Ordinal)
            .Replace("(", " ", StringComparison.Ordinal)
            .Replace(")", " ", StringComparison.Ordinal)
            .Replace("|", " ", StringComparison.Ordinal)
            .Replace("[", " ", StringComparison.Ordinal)
            .Replace("]", " ", StringComparison.Ordinal);

        return withoutEntities
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeToken)
            .Where(token => token.Length > 0)
            .ToArray();
    }

    private static string NormalizeToken(string token)
    {
        return token.Trim().TrimEnd('.', ',', '!', '?', ';').ToLowerInvariant();
    }
}
