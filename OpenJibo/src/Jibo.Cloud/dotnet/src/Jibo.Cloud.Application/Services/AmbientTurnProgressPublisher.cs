using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class AmbientTurnProgressPublisher(ILogger<AmbientTurnProgressPublisher>? logger = null)
    : ITurnProgressPublisher
{
    private static readonly AsyncLocal<Scope?> Current = new();
    private readonly ILogger _logger = logger ?? NullLogger<AmbientTurnProgressPublisher>.Instance;

    public static IDisposable Begin(Func<WebSocketReply, CancellationToken, Task> sendAsync)
    {
        var previous = Current.Value;
        Current.Value = new Scope(sendAsync, previous?.Turn, previous?.Session);
        return new Popper(previous);
    }

    public static void BindTurn(TurnContext turn, CloudSession session)
    {
        var scope = Current.Value;
        if (scope is null) return;
        Current.Value = scope.WithTurn(turn, session);
    }

    public Task PublishAsync(WebSocketReply reply, CancellationToken cancellationToken = default)
    {
        var scope = Current.Value;
        if (scope is null || string.IsNullOrWhiteSpace(reply.Text))
            return Task.CompletedTask;

        return scope.SendAsync(reply, cancellationToken);
    }

    public async Task PublishSearchThinkingAsync(CancellationToken cancellationToken = default)
    {
        var scope = Current.Value;
        if (scope is null) return;

        try
        {
            var transId = scope.ResolveTransId();
            if (string.IsNullOrWhiteSpace(transId)) return;

            if (scope.Session is null)
                return;

            var alreadySentPrelude = string.Equals(
                scope.Session.Metadata.TryGetValue(
                    SearchThinkingSkillActionFactory.PreludeMetadataKey,
                    out var existing)
                    ? existing?.ToString()
                    : null,
                transId,
                StringComparison.Ordinal);

            if (!alreadySentPrelude)
            {
                var transcript = scope.ResolveTranscript();
                var rules = SearchThinkingSkillActionFactory.ResolveRules(scope.Turn);
                foreach (var reply in SearchThinkingSkillActionFactory.CreateListenAndEos(transId, transcript, rules))
                    await scope.SendAsync(reply, cancellationToken);

                scope.Session.Metadata[SearchThinkingSkillActionFactory.PreludeMetadataKey] = transId;
            }

            await scope.SendAsync(
                new WebSocketReply { Text = SearchThinkingSkillActionFactory.CreateThinkingJson(transId) },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to publish search thinking progress.");
        }
    }

    private sealed class Scope(
        Func<WebSocketReply, CancellationToken, Task> sendAsync,
        TurnContext? turn,
        CloudSession? session)
    {
        public Func<WebSocketReply, CancellationToken, Task> SendAsync { get; } = sendAsync;
        public TurnContext? Turn { get; } = turn;
        public CloudSession? Session { get; } = session;

        public Scope WithTurn(TurnContext turnContext, CloudSession cloudSession) =>
            new(SendAsync, turnContext, cloudSession);

        public string ResolveTransId()
        {
            if (Turn?.Attributes.TryGetValue("transID", out var turnTransId) == true &&
                !string.IsNullOrWhiteSpace(turnTransId?.ToString()))
                return turnTransId.ToString()!;

            if (!string.IsNullOrWhiteSpace(Session?.TurnState.TransId))
                return Session.TurnState.TransId!;

            return Session?.LastTransId ?? string.Empty;
        }

        public string ResolveTranscript()
        {
            return Turn?.NormalizedTranscript
                   ?? Turn?.RawTranscript
                   ?? Session?.LastTranscript
                   ?? string.Empty;
        }
    }

    private sealed class Popper(Scope? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
