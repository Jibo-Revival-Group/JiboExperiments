namespace Jibo.Cloud.Infrastructure.Search;

internal static class SearchInstructionsResolver
{
    public static string? Resolve(string? inlineValue, string? filePath)
    {
        var inline = Normalize(inlineValue);
        if (inline is not null) return inline;

        if (string.IsNullOrWhiteSpace(filePath)) return null;

        foreach (var path in ResolveFileCandidates(filePath))
        {
            if (!File.Exists(path)) continue;
            return File.ReadAllText(path).Trim();
        }

        return null;
    }

    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().Replace("\\n", "\n", StringComparison.Ordinal);
    }

    private static IEnumerable<string> ResolveFileCandidates(string filePath)
    {
        if (Path.IsPathRooted(filePath))
            return [filePath];

        var candidates = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.GetFullPath(filePath)
        };

        foreach (var root in FindCandidateRoots())
            candidates.Add(Path.GetFullPath(Path.Combine(root, filePath)));

        return candidates;
    }

    private static IEnumerable<string> FindCandidateRoots()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var openJiboRoot = FindOpenJiboRoot(start);
            if (!string.IsNullOrWhiteSpace(openJiboRoot))
                yield return openJiboRoot;
        }
    }

    private static string? FindOpenJiboRoot(string startPath)
    {
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
}
