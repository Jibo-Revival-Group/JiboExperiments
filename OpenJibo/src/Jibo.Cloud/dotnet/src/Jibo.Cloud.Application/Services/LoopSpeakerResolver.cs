using System.Globalization;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Maps the robot's face/voice perception ids onto Loop members. Spoken
/// "my name is …" is not an identity source.
/// </summary>
public static class LoopSpeakerResolver
{
    public const string UnrecognizedReply =
        "I don't recognize you yet. Add someone in the People page, then say meet someone new so I can learn your face and voice.";

    public const string UnnamedRecognizedReply =
        "I recognize you, but I don't have a name for you yet. Add it in the People page.";

    public static LoopSpeakerIdentity Resolve(TurnContext turn, ICloudStateStore? cloudStateStore)
    {
        var presence = ReadPresence(turn);
        var speakerId = ResolveSpeakerId(turn, presence);
        if (string.IsNullOrWhiteSpace(speakerId))
            return LoopSpeakerIdentity.Unknown;

        var member = FindLoopMember(turn, cloudStateStore, speakerId);
        var displayName = FormatDisplayName(member?.Nickname)
                          ?? FormatDisplayName(member?.FirstName)
                          ?? FormatDisplayName(presence.LoopUserNames.TryGetValue(speakerId, out var contextName)
                              ? contextName
                              : null);

        return new LoopSpeakerIdentity(speakerId, member?.Id ?? speakerId, displayName);
    }

    private static string? ResolveSpeakerId(TurnContext turn, PerceptionPresence presence)
    {
        if (!string.IsNullOrWhiteSpace(presence.SpeakerId))
            return NormalizePersonId(presence.SpeakerId);

        var triggerLooperId = turn.Attributes.TryGetValue("triggerLooperId", out var rawTrigger)
            ? NormalizePersonId(rawTrigger?.ToString())
            : null;
        if (!string.IsNullOrWhiteSpace(triggerLooperId))
            return triggerLooperId;

        return presence.PeoplePresentIds.Count == 1
            ? NormalizePersonId(presence.PeoplePresentIds[0])
            : null;
    }

    private static LoopMemberRecord? FindLoopMember(TurnContext turn, ICloudStateStore? cloudStateStore,
        string speakerId)
    {
        if (cloudStateStore is null) return null;

        var loopId = ReadLoopId(turn);
        var members = cloudStateStore.GetLoopMembers(loopId);
        return members.FirstOrDefault(member => IsMatchingHouseholdMember(member, speakerId));
    }

    private static bool IsMatchingHouseholdMember(LoopMemberRecord member, string speakerId)
    {
        if (string.Equals(member.Type, "robot", StringComparison.OrdinalIgnoreCase)) return false;
        if (member.Status.Equals("removed", StringComparison.OrdinalIgnoreCase) ||
            member.Status.Equals("declined", StringComparison.OrdinalIgnoreCase))
            return false;

        return member.Id.Equals(speakerId, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(member.AccountId) &&
                member.AccountId.Equals(speakerId, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadLoopId(TurnContext turn)
    {
        if (turn.Attributes.TryGetValue("loopId", out var value) &&
            value is not null &&
            !string.IsNullOrWhiteSpace(value.ToString()))
            return value.ToString()!.Trim();

        return "openjibo-default-loop";
    }

    internal static string? FormatDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim());
    }

    private static string? NormalizePersonId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return string.Equals(trimmed, "NOT_TRAINED", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    private static PerceptionPresence ReadPresence(TurnContext turn)
    {
        if (!turn.Attributes.TryGetValue("context", out var contextValue) ||
            contextValue is null ||
            string.IsNullOrWhiteSpace(contextValue.ToString()))
            return PerceptionPresence.Empty;

        try
        {
            using var document = JsonDocument.Parse(contextValue.ToString()!);
            if (!document.RootElement.TryGetProperty("runtime", out var runtime) ||
                runtime.ValueKind != JsonValueKind.Object)
                return PerceptionPresence.Empty;

            var loopUserNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (runtime.TryGetProperty("loop", out var loop) &&
                loop.ValueKind == JsonValueKind.Object &&
                loop.TryGetProperty("users", out var users) &&
                users.ValueKind == JsonValueKind.Array)
                foreach (var user in users.EnumerateArray())
                {
                    var id = ReadString(user, "id");
                    var name = ReadString(user, "nickname", "nickName") ?? ReadString(user, "firstName");
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                        loopUserNames[id] = name;
                }

            var speakerId = string.Empty;
            var peoplePresentIds = new List<string>();
            if (runtime.TryGetProperty("perception", out var perception) &&
                perception.ValueKind == JsonValueKind.Object)
            {
                if (perception.TryGetProperty("speaker", out var speaker))
                    speakerId = speaker.ValueKind switch
                    {
                        JsonValueKind.String => speaker.GetString() ?? string.Empty,
                        JsonValueKind.Object => ReadString(speaker, "id", "looperID", "looperId") ?? string.Empty,
                        _ => speakerId
                    };

                if (perception.TryGetProperty("peoplePresent", out var peoplePresent) &&
                    peoplePresent.ValueKind == JsonValueKind.Array)
                    foreach (var person in peoplePresent.EnumerateArray())
                    {
                        var personId = person.ValueKind switch
                        {
                            JsonValueKind.String => person.GetString(),
                            JsonValueKind.Object => ReadString(person, "id", "looperID", "looperId"),
                            _ => null
                        };
                        var normalized = NormalizePersonId(personId);
                        if (!string.IsNullOrWhiteSpace(normalized))
                            peoplePresentIds.Add(normalized);
                    }
            }

            return new PerceptionPresence(
                NormalizePersonId(speakerId),
                peoplePresentIds,
                loopUserNames);
        }
        catch
        {
            return PerceptionPresence.Empty;
        }
    }

    private static string? ReadString(JsonElement source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            if (source.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString();

        return null;
    }

    private sealed record PerceptionPresence(
        string? SpeakerId,
        IReadOnlyList<string> PeoplePresentIds,
        IReadOnlyDictionary<string, string> LoopUserNames)
    {
        public static PerceptionPresence Empty { get; } = new(
            null,
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}

public sealed record LoopSpeakerIdentity(string SpeakerId, string MemberId, string? DisplayName)
{
    public static LoopSpeakerIdentity Unknown { get; } = new(string.Empty, string.Empty, null);

    public bool IsRecognized => !string.IsNullOrWhiteSpace(SpeakerId);
}
