using System.Text.Json;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class TurnAttributeReader
{
    public static IEnumerable<string> ReadRules(TurnContext turn, string key)
    {
        if (!turn.Attributes.TryGetValue(key, out var value) || value is null) yield break;

        foreach (var rule in ReadStringValues(value))
        {
            if (!string.IsNullOrWhiteSpace(rule))
                yield return rule;
        }
    }

    public static bool ReadBool(TurnContext turn, string key)
    {
        if (!turn.Attributes.TryGetValue(key, out var value) || value is null) return false;

        return value switch
        {
            bool boolean => boolean,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false
        };
    }

    private static IEnumerable<string> ReadStringValues(object value)
    {
        switch (value)
        {
            case string text:
                yield return text;
                yield break;
            case IReadOnlyList<string> typed:
                foreach (var item in typed) yield return item;
                yield break;
            case IEnumerable<string> strings:
                foreach (var item in strings) yield return item;
                yield break;
            case JsonElement { ValueKind: JsonValueKind.Array } json:
                foreach (var item in json.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        yield return item.GetString() ?? string.Empty;
                }

                yield break;
        }
    }
}
