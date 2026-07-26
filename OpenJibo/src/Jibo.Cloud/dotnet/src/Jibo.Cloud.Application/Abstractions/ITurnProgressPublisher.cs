using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Abstractions;

public interface ITurnProgressPublisher
{
    Task PublishAsync(WebSocketReply reply, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flush LISTEN + EOS so Nimbus can start its built-in Thinking_Eye wait
    /// before knowledge search HTTP begins. Do not send a SKILL_ACTION here —
    /// CloudResponseRegistry is one-shot and an interim action would steal the answer.
    /// </summary>
    Task PublishSearchThinkingPreludeAsync(CancellationToken cancellationToken = default);
}
