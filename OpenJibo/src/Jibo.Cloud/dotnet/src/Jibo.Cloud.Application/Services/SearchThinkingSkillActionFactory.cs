using System.Text.Json;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public static class SearchThinkingSkillActionFactory
{
    public const string PreludeMetadataKey = "searchThinkingPreludeTransId";

    public const string ThinkingAnimationEsml =
        "<speak><anim name='Thinking_Eye_Loop_01' nonBlocking='true'/></speak>";

    /// <summary>
    /// Brief pause after flushing the thinking action so the robot can begin playback
    /// before HTTP search starts. We cannot await CMD_RESULT (receive loop is blocked).
    /// Tests may set this to <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public static TimeSpan AnimStartGrace { get; set; } = TimeSpan.FromMilliseconds(350);

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

    public static string CreateThinkingJson(string transId)
    {
        var payload = new
        {
            type = "SKILL_ACTION",
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            msgID = CloudMessageIdFactory.CreateHubMessageId(),
            transID = transId,
            data = new
            {
                skill = new
                {
                    id = "chitchat-skill"
                },
                action = new
                {
                    config = new
                    {
                        jcp = new
                        {
                            type = "SLIM",
                            config = new
                            {
                                play = new
                                {
                                    esml = ThinkingAnimationEsml,
                                    meta = new
                                    {
                                        prompt_id = "RUNTIME_PROMPT",
                                        prompt_sub_category = "AN",
                                        mim_id = "runtime-search-thinking",
                                        mim_type = "announcement"
                                    }
                                }
                            }
                        }
                    }
                },
                analytics = new Dictionary<string, object?>(),
                final = false,
                fireAndForget = true
            }
        };

        return JsonSerializer.Serialize(payload);
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
