using System.Text.Json;

namespace Jibo.Cloud.Api.Hosting;

internal static class SocketMessageTypeReader
{
    internal static string Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "BINARY_OR_EMPTY";

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                ? type.GetString() ?? "UNKNOWN"
                : "UNKNOWN";
        }
        catch
        {
            return "TEXT";
        }
    }
}