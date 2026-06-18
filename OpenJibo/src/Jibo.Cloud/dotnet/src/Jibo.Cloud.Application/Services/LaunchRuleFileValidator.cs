namespace Jibo.Cloud.Application.Services;

public static class LaunchRuleFileValidator
{
    public const int MaxFileBytes = 64 * 1024;
    public const int MaxFilesPerRobot = 32;

    public static bool TryNormalizeFileName(string? value, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "File name is required.";
            return false;
        }

        normalized = Path.GetFileName(value.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "File name is invalid.";
            return false;
        }

        if (!normalized.EndsWith(".rule", StringComparison.OrdinalIgnoreCase))
        {
            error = "Launch rule files must use the .rule extension.";
            return false;
        }

        if (normalized.Any(ch => char.IsControl(ch)))
        {
            error = "File name contains invalid characters.";
            return false;
        }

        return true;
    }

    public static bool TryValidateContent(string content, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "Launch rule files cannot be empty.";
            return false;
        }

        if (content.Length > MaxFileBytes)
        {
            error = $"Launch rule files must be {MaxFileBytes} bytes or smaller.";
            return false;
        }

        return true;
    }
}
