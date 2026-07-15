using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Conversions;

public sealed class MeasurementConversionCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public IMeasurementConversionCatalog LoadFromJson(string json)
    {
        var entries = JsonSerializer.Deserialize<MeasurementConversionCatalogEntryDto[]>(json, JsonOptions) ?? [];
        return BuildCatalog(entries);
    }

    public IMeasurementConversionCatalog LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    internal static IMeasurementConversionCatalog BuildCatalog(IEnumerable<MeasurementConversionCatalogEntryDto> entries)
    {
        var lookup = new Dictionary<string, MeasurementConversionEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var conversion = entry.ToEntry();
            foreach (var smallAlias in entry.SmallUnit.Aliases)
            {
                foreach (var largeAlias in entry.LargeUnit.Aliases)
                {
                    var key = BuildKey(smallAlias, largeAlias);
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    lookup[key] = conversion;
                }
            }
        }

        return new MeasurementConversionCatalog(lookup);
    }

    internal static string BuildKey(string smallAlias, string largeAlias)
    {
        var small = NormalizePhrase(smallAlias);
        var large = NormalizePhrase(largeAlias);
        if (string.IsNullOrWhiteSpace(small) || string.IsNullOrWhiteSpace(large)) return string.Empty;
        return $"{small}|{large}";
    }

    internal static string NormalizePhrase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return string.Join(
            ' ',
            value.Trim().ToLowerInvariant().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    internal sealed class MeasurementConversionCatalogEntryDto
    {
        public MeasurementUnitDto SmallUnit { get; init; } = new();
        public MeasurementUnitDto LargeUnit { get; init; } = new();
        public double Count { get; init; }

        public MeasurementConversionEntry ToEntry()
        {
            return new MeasurementConversionEntry(
                new MeasurementUnit(SmallUnit.Singular, SmallUnit.Plural, SmallUnit.Aliases),
                new MeasurementUnit(LargeUnit.Singular, LargeUnit.Plural, LargeUnit.Aliases),
                Count);
        }
    }

    internal sealed class MeasurementUnitDto
    {
        public string Singular { get; init; } = string.Empty;
        public string Plural { get; init; } = string.Empty;
        public string[] Aliases { get; init; } = [];
    }
}

internal sealed class MeasurementConversionCatalog(IReadOnlyDictionary<string, MeasurementConversionEntry> lookup)
    : IMeasurementConversionCatalog
{
    public bool TryResolve(string smallUnitPhrase, string largeUnitPhrase, out MeasurementConversionEntry entry)
    {
        entry = null!;
        var key = MeasurementConversionCatalogLoader.BuildKey(smallUnitPhrase, largeUnitPhrase);
        if (string.IsNullOrWhiteSpace(key)) return false;
        return lookup.TryGetValue(key, out entry!);
    }
}
