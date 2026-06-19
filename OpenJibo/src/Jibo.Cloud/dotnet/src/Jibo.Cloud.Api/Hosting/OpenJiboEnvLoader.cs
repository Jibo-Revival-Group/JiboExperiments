namespace Jibo.Cloud.Api.Hosting;

internal static class OpenJiboEnvLoader
{
    public static void Load(string? startPath = null)
    {
        foreach (var envPath in ResolveEnvFileCandidates(startPath))
        {
            if (!File.Exists(envPath)) continue;

            LoadFile(envPath);
            return;
        }
    }

    private static IEnumerable<string> ResolveEnvFileCandidates(string? startPath)
    {
        var openJiboRoot = FindOpenJiboRepoRoot(startPath ?? Directory.GetCurrentDirectory()) ??
                           FindOpenJiboRepoRoot(AppContext.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(openJiboRoot))
            yield return Path.Combine(openJiboRoot, ".env");

        var workspaceRoot = FindWorkspaceRoot(startPath ?? Directory.GetCurrentDirectory()) ??
                            FindWorkspaceRoot(AppContext.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            yield return Path.Combine(workspaceRoot, ".env");
    }

    private static void LoadFile(string envPath)
    {
        foreach (var line in File.ReadLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0) continue;

            var key = trimmed[..separatorIndex].Trim();
            if (key.Length == 0) continue;

            var value = trimmed[(separatorIndex + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
                value = value[1..^1];

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? FindOpenJiboRepoRoot(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath)) return null;

        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        if (directory is { Exists: false, Parent: not null }) directory = directory.Parent;

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenJibo.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private static string? FindWorkspaceRoot(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath)) return null;

        var openJiboRoot = FindOpenJiboRepoRoot(startPath);
        if (string.IsNullOrWhiteSpace(openJiboRoot)) return null;

        var parent = Directory.GetParent(openJiboRoot);
        return parent?.Exists == true ? parent.FullName : null;
    }
}
