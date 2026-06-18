using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static partial class RobotFriendlyNameValidator
{
    public const int MaxLength = 128;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]{1,126}[A-Za-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex FriendlyNamePattern();

    public static bool TryNormalize(string? value, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Robot friendly name is required.";
            return false;
        }

        normalized = value.Trim();
        if (normalized.Length > MaxLength)
        {
            error = $"Robot friendly name must be {MaxLength} characters or fewer.";
            return false;
        }

        if (!FriendlyNamePattern().IsMatch(normalized))
        {
            error =
                "Use your robot's friendly name (letters, numbers, hyphens), such as Royal-Current-Sage-Canvas.";
            return false;
        }

        return true;
    }
}
