namespace Jibo.Cloud.Domain.Models;

/// <summary>
/// Declarative metadata for an installable skill package.
/// The manifest is independent of the runtime that executes the package.
/// </summary>
public sealed class SkillManifest
{
    public string SkillId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Runtime { get; init; } = string.Empty;
    public string ExecutionTarget { get; init; } = string.Empty;
    public IReadOnlyList<string> SupportedLanguages { get; init; } = [];
    public IReadOnlyList<SkillIntentBinding> IntentBindings { get; init; } = [];
    public IReadOnlyList<string> ProactiveBindings { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public SkillCompatibility Compatibility { get; init; } = new();
    public SkillAssets Assets { get; init; } = new();
}

public sealed class SkillIntentBinding
{
    public string Intent { get; init; } = string.Empty;
    public int Priority { get; init; }
    public SkillBindingMatch Match { get; init; } = new();
}

public sealed class SkillBindingMatch
{
    public IReadOnlyList<string> Entities { get; init; } = [];
    public IReadOnlyList<string> Contexts { get; init; } = [];
    public IReadOnlyList<string> Languages { get; init; } = [];
}

public sealed class SkillCompatibility
{
    public string ApiVersion { get; init; } = string.Empty;
    public string MinimumServerVersion { get; init; } = string.Empty;
}

public sealed class SkillAssets
{
    public IReadOnlyList<string> Paths { get; init; } = [];
}
