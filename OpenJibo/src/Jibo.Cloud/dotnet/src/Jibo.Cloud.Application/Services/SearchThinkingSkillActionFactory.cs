using System.Text.Json;

namespace Jibo.Cloud.Application.Services;

public static class SearchThinkingSkillActionFactory
{
    public const string ThinkingAnimationEsml =
        "<speak><anim name='eye_thinking_01' nonBlocking='true'/></speak>";

    public static string CreateJson(string transId)
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
                final = false
            }
        };

        return JsonSerializer.Serialize(payload);
    }
}
