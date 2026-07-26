using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

public sealed class AmbientTurnProgressPublisher : ITurnProgressPublisher
{
    private static readonly AsyncLocal<Scope?> Current = new();

    public static IDisposable Begin(
        Func<string> resolveTransId,
        Func<WebSocketReply, CancellationToken, Task> sendAsync)
    {
        var previous = Current.Value;
        Current.Value = new Scope(resolveTransId, sendAsync);
        return new Popper(previous);
    }

    public Task PublishAsync(WebSocketReply reply, CancellationToken cancellationToken = default)
    {
        var scope = Current.Value;
        if (scope is null || string.IsNullOrWhiteSpace(reply.Text))
            return Task.CompletedTask;

        return scope.SendAsync(reply, cancellationToken);
    }

    public Task PublishSearchThinkingAsync(CancellationToken cancellationToken = default)
    {
        var scope = Current.Value;
        if (scope is null) return Task.CompletedTask;

        var transId = scope.ResolveTransId();
        if (string.IsNullOrWhiteSpace(transId)) return Task.CompletedTask;

        var reply = new WebSocketReply
        {
            Text = SearchThinkingSkillActionFactory.CreateJson(transId)
        };
        return scope.SendAsync(reply, cancellationToken);
    }

    private sealed class Scope(
        Func<string> resolveTransId,
        Func<WebSocketReply, CancellationToken, Task> sendAsync)
    {
        public Func<string> ResolveTransId { get; } = resolveTransId;
        public Func<WebSocketReply, CancellationToken, Task> SendAsync { get; } = sendAsync;
    }

    private sealed class Popper(Scope? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
