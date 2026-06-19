using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Search;

public static class SearchBackendSpecParser
{
    public static SearchBackendSpec Parse(string? value)
    {
        return TryParse(value, out var spec) ? spec : SearchBackendSpec.None;
    }

    public static bool TryParse(string? value, out SearchBackendSpec spec)
    {
        spec = SearchBackendSpec.None;
        if (string.IsNullOrWhiteSpace(value)) return true;

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase))
            return true;

        var separatorIndex = trimmed.IndexOf('!');
        if (separatorIndex < 0)
        {
            if (!Enum.TryParse(trimmed, true, out SearchBackendKind backendOnly))
                return false;

            spec = new SearchBackendSpec(backendOnly, null, null);
            return true;
        }

        var backendToken = trimmed[..separatorIndex].Trim();
        if (!Enum.TryParse(backendToken, true, out SearchBackendKind backendKind))
            return false;

        var remainder = trimmed[(separatorIndex + 1)..];
        string? credential = null;
        string? model = null;

        var modelSeparatorIndex = remainder.IndexOf('!');
        if (modelSeparatorIndex < 0)
            credential = NullIfEmpty(remainder);
        else
        {
            credential = NullIfEmpty(remainder[..modelSeparatorIndex]);
            model = NullIfEmpty(remainder[(modelSeparatorIndex + 1)..]);
        }

        spec = new SearchBackendSpec(backendKind, credential, model);
        return true;
    }

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
