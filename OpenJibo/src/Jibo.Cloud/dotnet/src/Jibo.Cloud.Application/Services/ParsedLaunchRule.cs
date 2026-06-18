namespace Jibo.Cloud.Application.Services;

public sealed class ParsedLaunchRule
{
    public required string RuleName { get; init; }
    public required string SourceFile { get; init; }
    public required IReadOnlyList<string> LiteralTokens { get; init; }
    public required IReadOnlyDictionary<string, string> Entities { get; init; }
}
