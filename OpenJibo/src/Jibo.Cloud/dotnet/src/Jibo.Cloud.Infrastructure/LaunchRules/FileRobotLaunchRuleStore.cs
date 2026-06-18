using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.LaunchRules;

public sealed class FileRobotLaunchRuleStore(RobotLaunchRuleOptions options) : IRobotLaunchRuleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<RobotLaunchRuleFile> List(string robotFriendlyName)
    {
        var robotDirectory = GetRobotDirectory(robotFriendlyName);
        if (!Directory.Exists(robotDirectory)) return [];

        return Directory.EnumerateFiles(robotDirectory, "*.rule", SearchOption.TopDirectoryOnly)
            .Select(path => ReadRecord(robotFriendlyName, path))
            .Where(record => record is not null)
            .Cast<RobotLaunchRuleFile>()
            .OrderBy(record => record.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> ListRobotFriendlyNames()
    {
        var root = Path.GetFullPath(options.DirectoryPath);
        if (!Directory.Exists(root)) return [];

        return Directory.EnumerateDirectories(root)
            .Select(path => Path.GetFileName(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public RobotLaunchRuleFile? Get(string robotFriendlyName, string fileName)
    {
        if (!LaunchRuleFileValidator.TryNormalizeFileName(fileName, out var normalized, out _))
            return null;

        var path = Path.Combine(GetRobotDirectory(robotFriendlyName), normalized);
        return File.Exists(path) ? ReadRecord(robotFriendlyName, path) : null;
    }

    public RobotLaunchRuleFile Save(string robotFriendlyName, string fileName, string content)
    {
        if (!LaunchRuleFileValidator.TryNormalizeFileName(fileName, out var normalized, out var fileError))
            throw new InvalidOperationException(fileError);

        if (!LaunchRuleFileValidator.TryValidateContent(content, out var contentError))
            throw new InvalidOperationException(contentError);

        var robotDirectory = GetRobotDirectory(robotFriendlyName);
        Directory.CreateDirectory(robotDirectory);

        var existingCount = Directory.Exists(robotDirectory)
            ? Directory.EnumerateFiles(robotDirectory, "*.rule", SearchOption.TopDirectoryOnly).Count()
            : 0;

        var targetPath = Path.Combine(robotDirectory, normalized);
        if (!File.Exists(targetPath) && existingCount >= LaunchRuleFileValidator.MaxFilesPerRobot)
            throw new InvalidOperationException(
                $"Each robot can store up to {LaunchRuleFileValidator.MaxFilesPerRobot} launch rule files.");

        File.WriteAllText(targetPath, content, Encoding.UTF8);
        WriteMetadata(robotDirectory, normalized, content.Length);

        return ReadRecord(robotFriendlyName, targetPath)!;
    }

    public bool Delete(string robotFriendlyName, string fileName)
    {
        if (!LaunchRuleFileValidator.TryNormalizeFileName(fileName, out var normalized, out _))
            return false;

        var path = Path.Combine(GetRobotDirectory(robotFriendlyName), normalized);
        if (!File.Exists(path)) return false;

        File.Delete(path);
        RemoveMetadata(GetRobotDirectory(robotFriendlyName), normalized);
        return true;
    }

    private string GetRobotDirectory(string robotFriendlyName)
    {
        if (!RobotFriendlyNameValidator.TryNormalize(robotFriendlyName, out var normalized, out var error))
            throw new InvalidOperationException(error);

        var root = Path.GetFullPath(options.DirectoryPath);
        var robotDirectory = Path.GetFullPath(Path.Combine(root, normalized));
        if (!robotDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Robot friendly name is invalid.");

        return robotDirectory;
    }

    private static RobotLaunchRuleFile? ReadRecord(string robotFriendlyName, string path)
    {
        if (!File.Exists(path)) return null;

        var content = File.ReadAllText(path, Encoding.UTF8);
        var info = new FileInfo(path);
        return new RobotLaunchRuleFile
        {
            RobotFriendlyName = robotFriendlyName,
            FileName = info.Name,
            Content = content,
            SizeBytes = info.Length,
            UploadedUtc = info.LastWriteTimeUtc
        };
    }

    private static void WriteMetadata(string robotDirectory, string fileName, int sizeBytes)
    {
        var metadataPath = Path.Combine(robotDirectory, "metadata.json");
        var metadata = LoadMetadata(metadataPath);
        metadata[fileName] = new LaunchRuleMetadataEntry
        {
            UploadedUtc = DateTimeOffset.UtcNow,
            SizeBytes = sizeBytes
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8);
    }

    private static void RemoveMetadata(string robotDirectory, string fileName)
    {
        var metadataPath = Path.Combine(robotDirectory, "metadata.json");
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
