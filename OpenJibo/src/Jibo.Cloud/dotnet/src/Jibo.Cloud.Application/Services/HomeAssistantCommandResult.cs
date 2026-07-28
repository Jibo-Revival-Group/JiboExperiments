using System.Text.Json;

namespace Jibo.Cloud.Application.Services;

public sealed record HomeAssistantCommandResult(
    string RequestId,
    string Status,
    string? MatchedName = null,
    string? HeardName = null,
    IReadOnlyList<HomeAssistantCommandCandidate>? Candidates = null,
    string? Message = null,
    decimal? CurrentTemperature = null,
    string? Unit = null)
{
    public bool IsOk => string.Equals(Status, "ok", StringComparison.OrdinalIgnoreCase);
    public bool IsNotFound => string.Equals(Status, "not_found", StringComparison.OrdinalIgnoreCase);

    public bool NeedsClarification =>
        string.Equals(Status, "needs_clarification", StringComparison.OrdinalIgnoreCase);

    public static HomeAssistantCommandResult Timeout(string requestId) =>
        new(requestId, "error", Message: "timeout");

    public static HomeAssistantCommandResult FromJson(JsonElement root)
    {
        var requestId = root.TryGetProperty("requestId", out var requestElement)
            ? requestElement.GetString() ?? string.Empty
            : string.Empty;
        var status = root.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString() ?? "error"
            : "error";
        var matchedName = root.TryGetProperty("matchedName", out var matchedElement)
            ? matchedElement.GetString()
            : null;
        var heardName = root.TryGetProperty("heardName", out var heardElement)
            ? heardElement.GetString()
            : null;
        var message = root.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : null;
        decimal? currentTemperature = null;
        if (root.TryGetProperty("currentTemperature", out var currentTempElement) &&
            currentTempElement.ValueKind is JsonValueKind.Number)
        {
            if (currentTempElement.TryGetDecimal(out var parsedDecimal))
                currentTemperature = parsedDecimal;
            else if (currentTempElement.TryGetDouble(out var parsedDouble))
                currentTemperature = (decimal)parsedDouble;
        }

        var unit = root.TryGetProperty("unit", out var unitElement)
            ? unitElement.GetString()
            : null;

        List<HomeAssistantCommandCandidate>? candidates = null;
        if (root.TryGetProperty("candidates", out var candidatesElement) &&
            candidatesElement.ValueKind == JsonValueKind.Array)
        {
            candidates = [];
            foreach (var item in candidatesElement.EnumerateArray())
            {
                var entityId = item.TryGetProperty("entityId", out var idElement)
                    ? idElement.GetString()
                    : null;
                var name = item.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(entityId) || string.IsNullOrWhiteSpace(name))
                    continue;
                candidates.Add(new HomeAssistantCommandCandidate(entityId, name));
            }
        }

        return new HomeAssistantCommandResult(
            requestId,
            status,
            matchedName,
            heardName,
            candidates,
            message,
            currentTemperature,
            unit);
    }
}

public sealed record HomeAssistantCommandCandidate(string EntityId, string Name);
