namespace Jibo.Cloud.Application.Services;

internal static class LegacyMimDecisionMetadata
{
    internal static IDictionary<string, object?> BuildSkillPayload(
        LegacyMimSelection? selection,
        string defaultMimId = "runtime-chat")
    {
        if (selection is null) return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["mim_id"] = selection.MimId ?? defaultMimId,
            ["prompt_id"] = selection.PromptId
        };
    }

    internal static IDictionary<string, object?> ApplyEmotion(
        IDictionary<string, object?> updates,
        string? emotion)
    {
        if (!string.IsNullOrWhiteSpace(emotion))
            updates[ChitchatStateMachine.EmotionMetadataKey] = emotion;

        return updates;
    }
}
