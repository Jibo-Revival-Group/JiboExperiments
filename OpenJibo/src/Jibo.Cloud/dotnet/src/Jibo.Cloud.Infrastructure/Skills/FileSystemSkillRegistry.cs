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
            if (manifest is not null && !string.IsNullOrWhiteSpace(manifest.RoutingFile))
            {
                var routingPath = ResolvePackageFile(packageDirectory, manifest.RoutingFile);
                if (routingPath is null)
                    return Failed(packageDirectory, "routingFile must point to a file inside the skill package.");

                if (!File.Exists(routingPath))
                    return Failed(packageDirectory, $"routing file '{manifest.RoutingFile}' is missing.");

                var routing = JsonSerializer.Deserialize<SkillRoutingFile>(File.ReadAllText(routingPath), JsonOptions);
                if (routing is null)
                    return Failed(packageDirectory, $"routing file '{manifest.RoutingFile}' is empty.");

                manifest.IntentBindings = routing.Bindings;
            }

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
        {
            Require(manifest.Adapter, "adapter", errors);
            Require(manifest.RoutingFile, "routingFile", errors);
        }

        if (!string.Equals(manifest.ExecutionTarget, "server", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(manifest.ExecutionTarget, "robot", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(manifest.ExecutionTarget, "both", StringComparison.OrdinalIgnoreCase))
            errors.Add("executionTarget must be server, robot, or both.");

        if (manifest.IntentBindings.Any(binding => string.IsNullOrWhiteSpace(binding.Intent)))
            errors.Add("every intent binding must declare an intent.");

        return errors;
    }

    private static string? ResolvePackageFile(string packageDirectory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) return null;

        var packageRoot = Path.GetFullPath(packageDirectory);
        var candidate = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
        return candidate.StartsWith(packageRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
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
