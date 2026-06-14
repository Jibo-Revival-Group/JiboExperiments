namespace Jibo.Cloud.Application.Services;

public sealed record JiboInteractionDecision(
    string IntentName,
    string ReplyText,
    string? SkillName = null,
    IDictionary<string, object?>? SkillPayload = null,
    IDictionary<string, object?>? ContextUpdates = null);