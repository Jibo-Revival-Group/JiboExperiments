using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Holidays;

public sealed class HolidayCountdownCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public IHolidayCountdownCatalog LoadFromJson(string json)
    {
        var entries = JsonSerializer.Deserialize<HolidayCountdownCatalogEntryDto[]>(json, JsonOptions) ?? [];
        return BuildCatalog(entries);
    }

    public IHolidayCountdownCatalog LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    internal static IHolidayCountdownCatalog BuildCatalog(IEnumerable<HolidayCountdownCatalogEntryDto> entries)
    {
        var aliases = new Dictionary<string, HolidayCountdownEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var holiday = new HolidayCountdownEntry(entry.CanonicalName, entry.Rule);
            foreach (var alias in entry.Aliases)
            {
                var normalized = NormalizePhrase(alias);
                if (string.IsNullOrWhiteSpace(normalized)) continue;
                aliases[normalized] = holiday;
            }
        }

        return new HolidayCountdownCatalog(aliases);
    }

    internal static string NormalizePhrase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var normalized = string.Join(
            ' ',
            value.Trim().ToLowerInvariant().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return normalized.StartsWith("the ", StringComparison.Ordinal) ? normalized[4..] : normalized;
    }

    internal sealed class HolidayCountdownCatalogEntryDto
    {
        public string CanonicalName { get; init; } = string.Empty;
        public HolidayDateRule Rule { get; init; } = new() { Type = string.Empty };
        public string[] Aliases { get; init; } = [];
    }
}

internal sealed class HolidayCountdownCatalog(IReadOnlyDictionary<string, HolidayCountdownEntry> aliases)
    : IHolidayCountdownCatalog
{
    public bool TryResolve(string normalizedPhrase, out HolidayCountdownEntry entry)
    {
        entry = null!;
        var normalized = HolidayCountdownCatalogLoader.NormalizePhrase(normalizedPhrase);
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        return aliases.TryGetValue(normalized, out entry!);
    }
}
