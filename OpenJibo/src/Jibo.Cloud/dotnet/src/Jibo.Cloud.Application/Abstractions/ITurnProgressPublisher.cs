using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Abstractions;

public interface ITurnProgressPublisher
{
    Task PublishAsync(WebSocketReply reply, CancellationToken cancellationToken = default);

    Task PublishSearchThinkingAsync(CancellationToken cancellationToken = default);
}
