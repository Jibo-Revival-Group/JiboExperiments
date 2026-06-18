using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.LaunchRules;

public sealed class FileRobotLaunchRuleStore(RobotLaunchRuleOptions options) : IRobotLaunchRuleStore
{
    public const string GlobalScopeName = "global";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<RobotLaunchRuleFile> List()
    {
        var directory = GetRulesDirectory();
        if (!Directory.Exists(directory)) return [];

        MigrateLegacyRobotDirectories(directory);

        return Directory.EnumerateFiles(directory, "*.rule", SearchOption.TopDirectoryOnly)
            .Select(ReadRecord)
            .Where(record => record is not null)
            .Cast<RobotLaunchRuleFile>()
            .OrderBy(record => record.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public RobotLaunchRuleFile? Get(string fileName)
    {
        if (!LaunchRuleFileValidator.TryNormalizeFileName(fileName, out var normalized, out _))
            return null;

        var path = Path.Combine(GetRulesDirectory(), normalized);
        return File.Exists(path) ? ReadRecord(path) : null;
    }

    public RobotLaunchRuleFile Save(string fileName, string content)
    {
        if (!LaunchRuleFileValidator.TryNormalizeFileName(fileName, out var normalized, out var fileError))
            throw new InvalidOperationException(fileError);

        if (!LaunchRuleFileValidator.TryValidateContent(content, out var contentError))
            throw new InvalidOperationException(contentError);

        var directory = GetRulesDirectory();
        Directory.CreateDirectory(directory);

        var existingCount = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.rule", SearchOption.TopDirectoryOnly).Count()
            : 0;

        var targetPath = Path.Combine(directory, normalized);
        if (!File.Exists(targetPath) && existingCount >= LaunchRuleFileValidator.MaxFiles)
            throw new InvalidOperationException(
                $"You can store up to {LaunchRuleFileValidator.MaxFiles} launch rule files.");

        File.WriteAllText(targetPath, content, Encoding.UTF8);
        WriteMetadata(directory, normalized, content.Length);

        return ReadRecord(targetPath)!;
    }

    public bool Delete(string fileName)
    {
        if (!LaunchRuleFileValidator.TryNormalizeFileName(fileName, out var normalized, out _))
            return false;

        var directory = GetRulesDirectory();
        var path = Path.Combine(directory, normalized);
        if (!File.Exists(path)) return false;

        File.Delete(path);
        RemoveMetadata(directory, normalized);
        return true;
    }

    private string GetRulesDirectory()
    {
        return Path.GetFullPath(options.DirectoryPath);
    }

    private static void MigrateLegacyRobotDirectories(string root)
    {
        foreach (var subDirectory in Directory.EnumerateDirectories(root))
        {
            foreach (var legacyRulePath in Directory.EnumerateFiles(subDirectory, "*.rule", SearchOption.TopDirectoryOnly))
            {
                var destination = Path.Combine(root, Path.GetFileName(legacyRulePath));
                if (File.Exists(destination)) continue;

                File.Move(legacyRulePath, destination);
            }

            if (!Directory.EnumerateFileSystemEntries(subDirectory).Any())
                Directory.Delete(subDirectory);
        }
    }

    private RobotLaunchRuleFile? ReadRecord(string path)
    {
        if (!File.Exists(path)) return null;

        var content = File.ReadAllText(path, Encoding.UTF8);
        var info = new FileInfo(path);
        return new RobotLaunchRuleFile
        {
            RobotFriendlyName = GlobalScopeName,
            FileName = info.Name,
            Content = content,
            SizeBytes = info.Length,
            UploadedUtc = info.LastWriteTimeUtc
        };
    }

    private static void WriteMetadata(string rulesDirectory, string fileName, int sizeBytes)
    {
        var metadataPath = Path.Combine(rulesDirectory, "metadata.json");
        var metadata = LoadMetadata(metadataPath);
        metadata[fileName] = new LaunchRuleMetadataEntry
        {
            UploadedUtc = DateTimeOffset.UtcNow,
            SizeBytes = sizeBytes
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8);
    }

    private static void RemoveMetadata(string rulesDirectory, string fileName)
    {
        var metadataPath = Path.Combine(rulesDirectory, "metadata.json");
        if (!File.Exists(metadataPath)) return;

        var metadata = LoadMetadata(metadataPath);
        metadata.Remove(fileName);
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8);
    }

    private static Dictionary<string, LaunchRuleMetadataEntry> LoadMetadata(string metadataPath)
    {
        if (!File.Exists(metadataPath)) return new Dictionary<string, LaunchRuleMetadataEntry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, LaunchRuleMetadataEntry>>(
                       File.ReadAllText(metadataPath, Encoding.UTF8), JsonOptions)
                   ?? new Dictionary<string, LaunchRuleMetadataEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, LaunchRuleMetadataEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class LaunchRuleMetadataEntry
    {
        public DateTimeOffset UploadedUtc { get; init; }
        public int SizeBytes { get; init; }
    }
}
