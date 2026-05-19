namespace Jibo.Cloud.Infrastructure.Media;

internal static class MediaPathHelper
{
    public static string GetRelativeStoragePath(string path)
    {
        var trimmed = path.Trim().TrimStart('/', '\\');
        var segments = trimmed
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return segments.Length == 0 ? "media-item" : Path.Combine(segments);
    }

    private static string SanitizeSegment(string value)
    {
        var chars = value
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_')
            .ToArray();
        return string.Join(string.Empty, chars);
    }
}