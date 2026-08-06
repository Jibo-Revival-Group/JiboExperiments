using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private const string RepeatReplayAttributeKey = "repeatReplay";

    // Robot-side NLU results are what several routes key off, so a replay has to restore
    // the originals instead of inheriting the ones from the "do that again" turn.
    private static readonly string[] RepeatSnapshotAttributeKeys =
    [
        "clientIntent",
        "clientRules",
        "clientEntities",
        "listenRules",
        "listenAsrHints"
    ];

    private static readonly string[] NonRepeatableIntents =
    [
        "repeat_last_command",
        "trigger_ignored",
        "yes_no_clarify",
        "ha_climate_clarify"
    ];

    private async Task<JiboInteractionDecision> BuildRepeatLastCommandDecisionAsync(TurnContext turn,
        CancellationToken cancellationToken)
    {
        var (deviceId, friendlyId) = ResolveRepeatRobotKey(turn);
        var lastCommand = repeatLastCommandStore?.TryGetAndRenew(deviceId, friendlyId);

        if (lastCommand is null)
            return new JiboInteractionDecision(
                "repeat_last_command",
                "I don't remember what you asked me to do. What would you like?");

        return await BuildDecisionCoreAsync(CreateReplayTurn(turn, lastCommand), cancellationToken);
    }

    private void RecordRepeatableCommand(TurnContext turn, JiboInteractionDecision decision)
    {
        if (repeatLastCommandStore is null) return;

        var transcript = (turn.RawTranscript ?? turn.NormalizedTranscript ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(transcript)) return;

        if (turn.Attributes.TryGetValue("messageType", out var messageType) &&
            string.Equals(messageType?.ToString(), "TRIGGER", StringComparison.OrdinalIgnoreCase))
            return;

        // Mid-dialog fragments ("yes", "seven a.m.") only make sense inside their original
        // prompt, so replaying them in isolation would misroute.
        if (IsYesNoTurn(turn)) return;

        if (RepeatLastCommandParser.IsRepeatRequest(transcript)) return;

        if (NonRepeatableIntents.Contains(decision.IntentName, StringComparer.OrdinalIgnoreCase)) return;

        var (deviceId, friendlyId) = ResolveRepeatRobotKey(turn);
        var robotKey = !string.IsNullOrWhiteSpace(friendlyId) ? friendlyId : deviceId;
        if (string.IsNullOrWhiteSpace(robotKey)) return;

        repeatLastCommandStore.Set(
            robotKey,
            new RepeatLastCommandStore.LastCommand(
                turn.RawTranscript ?? transcript,
                turn.NormalizedTranscript,
                SnapshotNluAttributes(turn)));
    }

    private static TurnContext CreateReplayTurn(TurnContext turn, RepeatLastCommandStore.LastCommand lastCommand)
    {
        var attributes = new Dictionary<string, object?>(turn.Attributes, StringComparer.OrdinalIgnoreCase);

        foreach (var key in RepeatSnapshotAttributeKeys)
        {
            attributes.Remove(key);
            if (lastCommand.NluAttributes.TryGetValue(key, out var value)) attributes[key] = value;
        }

        attributes[RepeatReplayAttributeKey] = true;

        return new TurnContext
        {
            TurnId = turn.TurnId,
            SessionId = turn.SessionId,
            TimestampUtc = turn.TimestampUtc,
            InputMode = turn.InputMode,
            SourceKind = turn.SourceKind,
            WakePhrase = turn.WakePhrase,
            RawTranscript = lastCommand.RawTranscript,
            NormalizedTranscript = lastCommand.NormalizedTranscript,
            DeviceId = turn.DeviceId,
            HostName = turn.HostName,
            RequestId = turn.RequestId,
            ProtocolService = turn.ProtocolService,
            ProtocolOperation = turn.ProtocolOperation,
            FirmwareVersion = turn.FirmwareVersion,
            ApplicationVersion = turn.ApplicationVersion,
            Locale = turn.Locale,
            TimeZone = turn.TimeZone,
            IsFollowUpEligible = turn.IsFollowUpEligible,
            Attributes = attributes
        };
    }

    private static IReadOnlyDictionary<string, object?> SnapshotNluAttributes(TurnContext turn)
    {
        var snapshot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in RepeatSnapshotAttributeKeys)
            if (turn.Attributes.TryGetValue(key, out var value) && value is not null)
                snapshot[key] = value;

        return snapshot;
    }

    private (string? DeviceId, string? FriendlyId) ResolveRepeatRobotKey(TurnContext turn)
    {
        if (cloudStateStore is not null) return JiboIdentityResolver.Resolve(turn, cloudStateStore);

        foreach (var key in new[] { "friendlyId", "robotFriendlyId", "robotID", "robotId" })
            if (turn.Attributes.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value?.ToString()))
            {
                var candidate = value!.ToString()!.Trim();
                return (candidate, candidate);
            }

        var fallback = !string.IsNullOrWhiteSpace(turn.DeviceId) ? turn.DeviceId.Trim() : turn.SessionId.Trim();
        return (fallback, fallback);
    }
}
