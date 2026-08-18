using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Abstractions;

public interface ISkillRegistry
{
    string SkillsDirectory { get; }
    IReadOnlyList<InstalledSkill> GetInstalledSkills();
    void Refresh();
}

public sealed class InstalledSkill
{
    public string PackageDirectory { get; init; } = string.Empty;
    public SkillManifest? Manifest { get; init; }
    public SkillLifecycleState State { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

public enum SkillLifecycleState
{
    Uploaded,
    Validated,
    Installed,
    Enabled,
    Failed
}
