using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.Skills;

/// <summary>
/// Reads installed skill package metadata from App_Data/Skills.
/// It does not load or execute package code.
/// </summary>
public sealed class FileSystemSkillRegistry : ISkillRegistry
{
    private const string ManifestFileName = "manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly object _sync = new();
    private readonly ILogger<FileSystemSkillRegistry> _logger;
    private IReadOnlyList<InstalledSkill> _skills = [];

    public FileSystemSkillRegistry(string skillsDirectory, ILogger<FileSystemSkillRegistry> logger)
    {
        if (string.IsNullOrWhiteSpace(skillsDirectory))
            throw new ArgumentException("A skills directory is required.", nameof(skillsDirectory));

        SkillsDirectory = Path.GetFullPath(skillsDirectory);
        _logger = logger;
        Refresh();
    }

    public string SkillsDirectory { get; }

    public IReadOnlyList<InstalledSkill> GetInstalledSkills()
    {
        lock (_sync)
            return _skills;
    }

    public void Refresh()
    {
        Directory.CreateDirectory(SkillsDirectory);

        var discovered = new List<InstalledSkill>();
        foreach (var packageDirectory in Directory.EnumerateDirectories(SkillsDirectory).OrderBy(path => path,
                     StringComparer.OrdinalIgnoreCase))
        {
            discovered.Add(LoadPackage(packageDirectory));
        }

        foreach (var duplicate in discovered
                     .Where(skill => skill.Manifest is not null)
                     .GroupBy(skill => skill.Manifest!.SkillId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            foreach (var skill in duplicate)
            {
                var errors = skill.ValidationErrors.ToList();
                errors.Add($"skillId '{duplicate.Key}' is declared by more than one package.");
                discovered[discovered.IndexOf(skill)] = new InstalledSkill
                {
                    PackageDirectory = skill.PackageDirectory,
                    Manifest = skill.Manifest,
                    State = SkillLifecycleState.Failed,
                    ValidationErrors = errors
                };
            }
        }

        lock (_sync)
            _skills = discovered;

        _logger.LogInformation("Skill registry refreshed directory={SkillsDirectory} packages={PackageCount} enabled={EnabledCount}",
            SkillsDirectory,
            discovered.Count,
            discovered.Count(skill => skill.State == SkillLifecycleState.Enabled));
    }

    private InstalledSkill LoadPackage(string packageDirectory)
    {
        var manifestPath = Path.Combine(packageDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
            return Failed(packageDirectory, "manifest.json is missing.");

        try
        {
            var manifest = JsonSerializer.Deserialize<SkillManifest>(File.ReadAllText(manifestPath), JsonOptions);
            var errors = Validate(manifest);
            if (errors.Count > 0)
                return new InstalledSkill
                {
                    PackageDirectory = packageDirectory,
                    Manifest = manifest,
                    State = SkillLifecycleState.Failed,
                    ValidationErrors = errors
                };

            return new InstalledSkill
            {
                PackageDirectory = packageDirectory,
                Manifest = manifest,
                State = SkillLifecycleState.Enabled
            };
        }
        catch (JsonException exception)
        {
            return Failed(packageDirectory, $"manifest.json is invalid JSON: {exception.Message}");
        }
        catch (IOException exception)
        {
            return Failed(packageDirectory, $"manifest.json could not be read: {exception.Message}");
        }
    }

    private static List<string> Validate(SkillManifest? manifest)
    {
        var errors = new List<string>();
        if (manifest is null)
        {
            errors.Add("manifest.json is empty.");
            return errors;
        }

        Require(manifest.SkillId, "skillId", errors);
        Require(manifest.Name, "name", errors);
        Require(manifest.Version, "version", errors);
        Require(manifest.Runtime, "runtime", errors);
        Require(manifest.ExecutionTarget, "executionTarget", errors);

        if (!string.Equals(manifest.PackageType, "external", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(manifest.PackageType, "builtin", StringComparison.OrdinalIgnoreCase))
            errors.Add("packageType must be external or builtin.");

        if (string.Equals(manifest.PackageType, "builtin", StringComparison.OrdinalIgnoreCase))
            Require(manifest.Adapter, "adapter", errors);

        if (!string.Equals(manifest.ExecutionTarget, "server", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(manifest.ExecutionTarget, "robot", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(manifest.ExecutionTarget, "both", StringComparison.OrdinalIgnoreCase))
            errors.Add("executionTarget must be server, robot, or both.");

        if (manifest.IntentBindings.Any(binding => string.IsNullOrWhiteSpace(binding.Intent)))
            errors.Add("every intent binding must declare an intent.");

        return errors;
    }

    private static void Require(string? value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{name} is required.");
    }

    private static InstalledSkill Failed(string packageDirectory, string error) => new()
    {
        PackageDirectory = packageDirectory,
        State = SkillLifecycleState.Failed,
        ValidationErrors = [error]
    };
}
