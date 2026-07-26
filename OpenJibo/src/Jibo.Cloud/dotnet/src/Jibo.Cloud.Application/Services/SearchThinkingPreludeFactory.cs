using System.Text.Json;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public static class SearchThinkingPreludeFactory
{
    public const string PreludeMetadataKey = "searchThinkingPreludeTransId";

    /// <summary>
    /// Pegasus answer skill id on the wire. The robot remaps this to
    /// match.cloudSkill="answer" and match.skillID="@be/nimbus".
    /// </summary>
    public const string AnswerSkillId = "answer";

    public static IReadOnlyList<WebSocketReply> CreateListenAndEos(
        string transId,
        string transcript,
        IReadOnlyList<string> rules)
    {
        // Pegasus order for cloud skills: EOS, then non-final LISTEN, then (later) SKILL_ACTION.
        var eosMessage = new
        {
            type = "EOS",
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            msgID = CloudMessageIdFactory.CreateHubMessageId(),
            transID = transId,
            data = new { }
        };

        var listenMessage = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["type"] = "LISTEN",
            ["transID"] = transId,
            // Cloud skill: more messages coming (SKILL_ACTION). Matches Pegasus hub.
            ["final"] = false,
            ["data"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["asr"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["confidence"] = 0.95,
                    ["final"] = true,
                    ["text"] = transcript
                },
                ["nlu"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["confidence"] = 0.95,
                    ["intent"] = "knowledge_search",
                    ["rules"] = rules,
                    ["entities"] = new Dictionary<string, object?>()
                },
                // Do NOT put cloudSkill or @be/nimbus on the wire — robot remaps
                // skillID "answer" → cloudSkill "answer" + skillID "@be/nimbus".
                ["match"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["skillID"] = AnswerSkillId,
                    ["launch"] = true,
                    ["onRobot"] = false,
                    ["skipSurprises"] = true
                }
            }
        };

        return
        [
            new WebSocketReply { Text = JsonSerializer.Serialize(eosMessage) },
            new WebSocketReply { Text = JsonSerializer.Serialize(listenMessage) }
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
