using System.Text.Json;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public static class SearchThinkingPreludeFactory
{
    public const string PreludeMetadataKey = "searchThinkingPreludeTransId";
    public const string NimbusSkillId = "@be/nimbus";
    public const string AnswerCloudSkill = "answer";

    public static IReadOnlyList<WebSocketReply> CreateListenAndEos(
        string transId,
        string transcript,
        IReadOnlyList<string> rules)
    {
        var listenMessage = new
        {
            type = "LISTEN",
            transID = transId,
            data = new
            {
                asr = new
                {
                    confidence = 0.95,
                    final = true,
                    text = transcript
                },
                nlu = new
                {
                    confidence = 0.95,
                    intent = "knowledge_search",
                    rules,
                    entities = new Dictionary<string, object?>()
                },
                match = new
                {
                    intent = "knowledge_search",
                    rule = rules.FirstOrDefault() ?? string.Empty,
                    score = 0.95,
                    // Stock Nimbus ProcessCloud plays Thinking_Eye_Loop_01 while awaiting
                    // the single cloudSkillResponse when cloudSkill is answer/news.
                    skillID = NimbusSkillId,
                    onRobot = false,
                    cloudSkill = AnswerCloudSkill,
                    skipSurprises = true
                }
            }
        };

        var eosMessage = new
        {
            type = "EOS",
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            msgID = CloudMessageIdFactory.CreateHubMessageId(),
            transID = transId,
            data = new { }
        };

        return
        [
            new WebSocketReply { Text = JsonSerializer.Serialize(listenMessage) },
            new WebSocketReply { Text = JsonSerializer.Serialize(eosMessage) }
        ];
    }

    public static IReadOnlyList<string> ResolveRules(TurnContext? turn)
    {
        if (turn is null) return [];

        var messageType = turn.Attributes.TryGetValue("messageType", out var messageTypeValue)
            ? messageTypeValue?.ToString()
            : null;
        var attributeName = string.Equals(messageType, "CLIENT_NLU", StringComparison.OrdinalIgnoreCase)
            ? "clientRules"
            : "listenRules";

        if (!turn.Attributes.TryGetValue(attributeName, out var rulesValue) &&
            !turn.Attributes.TryGetValue("rules", out rulesValue))
            return [];

        return rulesValue switch
        {
            IReadOnlyList<string> typedRules => typedRules,
            IEnumerable<string> stringRules =>
                stringRules.Where(rule => !string.IsNullOrWhiteSpace(rule)).ToArray(),
            IEnumerable<object?> objectRules => objectRules
                .Select(rule => rule?.ToString())
                .Where(rule => !string.IsNullOrWhiteSpace(rule))
                .Cast<string>()
                .ToArray(),
            _ => []
        };
    }
}
