using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private JiboInteractionDecision BuildRememberNameDecision(TurnContext turn, string transcript)
    {
        var name = TryExtractNameFact(transcript);
        if (string.IsNullOrWhiteSpace(name))
            return new JiboInteractionDecision(
                "memory_set_name",
                "I can remember it if you say, my name is Alex.");

        personalMemoryStore.SetName(ResolveTenantScope(turn), name);
        return new JiboInteractionDecision(
            "memory_set_name",
            $"Nice to meet you, {name}. I will remember your name.");
    }

    private JiboInteractionDecision BuildRecallNameDecision(TurnContext turn, GreetingPresenceProfile? presence = null)
    {
        var personScope = ResolveTenantScope(turn, presence?.PrimaryPersonId);
        var name = personalMemoryStore.GetName(personScope);
        if (string.IsNullOrWhiteSpace(name) && CanUseLoopLevelNameMemoryFallback(presence))
            name = personalMemoryStore.GetName(ResolveTenantScope(turn));

        name = ToDisplayName(name ?? string.Empty);

        return string.IsNullOrWhiteSpace(name)
            ? new JiboInteractionDecision(
                "memory_get_name",
                "I do not know your name yet. You can say, my name is Alex.")
            : new JiboInteractionDecision(
                "memory_get_name",
                presence is not null && !string.IsNullOrWhiteSpace(presence.PrimaryPersonId)
                    ? $"I think you are {name}."
                    : $"You told me your name is {name}.");
    }

    private static bool CanUseLoopLevelNameMemoryFallback(GreetingPresenceProfile? presence)
    {
        if (presence is null) return true;
        if (string.IsNullOrWhiteSpace(presence.PrimaryPersonId)) return true;

        return presence.PeoplePresentIds.Count <= 1;
    }

    private JiboInteractionDecision BuildRememberBirthdayDecision(TurnContext turn, string transcript)
    {
        var birthday = TryExtractBirthdayFact(transcript);
        if (string.IsNullOrWhiteSpace(birthday))
            return new JiboInteractionDecision(
                "memory_set_birthday",
                "I can remember it if you say, my birthday is March 14.");

        var tenantScope = ResolveTenantScope(turn);
        personalMemoryStore.SetBirthday(tenantScope, birthday);
        var birthdayDate = TryParseBirthdayDate(birthday);
        if (birthdayDate is null)
            return new JiboInteractionDecision(
                "memory_set_birthday",
                $"Got it. I will remember your birthday is {birthday}.");

        var birthdayLabel = ResolvePreferredBirthdayLabel(turn);
        cloudStateStore?.UpsertHoliday(new HolidayRecord
        {
            EventId = $"birthday-{tenantScope.LoopId}-{tenantScope.PersonId ?? "loop"}",
            Name = string.IsNullOrWhiteSpace(birthdayLabel) ? "Birthday" : $"{birthdayLabel}'s Birthday",
            Category = "birthday",
            Subcategory = "personal",
            LoopId = tenantScope.LoopId,
            MemberId = tenantScope.PersonId,
            IsEnabled = true,
            Date = birthdayDate.Value,
            Source = "birthday",
            CountryCode = "US"
        });

        return new JiboInteractionDecision(
            "memory_set_birthday",
            $"Got it. I will remember your birthday is {birthday}.");
    }

    private JiboInteractionDecision BuildRecallBirthdayDecision(TurnContext turn)
    {
        var birthday = personalMemoryStore.GetBirthday(ResolveTenantScope(turn));
        return string.IsNullOrWhiteSpace(birthday)
            ? new JiboInteractionDecision(
                "memory_get_birthday",
                "I do not know your birthday yet. You can say, my birthday is March 14.")
            : new JiboInteractionDecision(
                "memory_get_birthday",
                $"You told me your birthday is {birthday}.");
    }

    private static DateOnly? TryParseBirthdayDate(string birthdayText)
    {
        if (string.IsNullOrWhiteSpace(birthdayText)) return null;

        var normalized = birthdayText.Trim().ToLowerInvariant();
        var match = Regex.Match(
            normalized,
            @"\b(?<month>january|february|march|april|may|june|july|august|september|october|november|december)\s+(?<day>\d{1,2})(?:st|nd|rd|th)?\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var month = match.Groups["month"].Value.ToLowerInvariant() switch
        {
            "january" => 1,
            "february" => 2,
            "march" => 3,
            "april" => 4,
            "may" => 5,
            "june" => 6,
            "july" => 7,
            "august" => 8,
            "september" => 9,
            "october" => 10,
            "november" => 11,
            "december" => 12,
            _ => 0
        };
        if (month == 0) return null;

        if (!int.TryParse(match.Groups["day"].Value, out var day) || day is < 1 or > 31) return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var year = today.Year;
        if (day > DateTime.DaysInMonth(year, month)) return null;

        DateOnly birthday;
        try
        {
            birthday = new DateOnly(year, month, day);
        }
        catch
        {
            return null;
        }

        if (birthday < today) birthday = birthday.AddYears(1);
        return birthday;
    }

    private static string? ResolvePreferredBirthdayLabel(TurnContext turn)
    {
        var context = ResolveGreetingPresenceProfile(turn);
        return !string.IsNullOrWhiteSpace(context.PrimaryPersonId) &&
               context.LoopUserFirstNames.TryGetValue(context.PrimaryPersonId, out var firstName) &&
               !string.IsNullOrWhiteSpace(firstName)
            ? ToDisplayName(firstName)
            : null;
    }

    private JiboInteractionDecision BuildRememberImportantDateDecision(TurnContext turn, string transcript)
    {
        var importantDate = TryExtractImportantDateSet(transcript);
        if (importantDate is null)
            return new JiboInteractionDecision(
                "memory_set_important_date",
                "I can remember it if you say, our anniversary is June 10.");

        personalMemoryStore.SetImportantDate(ResolveTenantScope(turn), importantDate.Value.Label,
            importantDate.Value.Value);
        return new JiboInteractionDecision(
            "memory_set_important_date",
            $"Got it. I will remember your {importantDate.Value.Label} is {importantDate.Value.Value}.");
    }

    private JiboInteractionDecision BuildRecallImportantDateDecision(TurnContext turn, string transcript)
    {
        var label = TryExtractImportantDateLookupLabel(transcript);
        if (string.IsNullOrWhiteSpace(label))
            return new JiboInteractionDecision(
                "memory_get_important_date",
                "Ask me like this: when is our anniversary?");

        var storedDate = personalMemoryStore.GetImportantDate(ResolveTenantScope(turn), label);
        return string.IsNullOrWhiteSpace(storedDate)
            ? new JiboInteractionDecision(
                "memory_get_important_date",
                $"I do not know your {label} yet.")
            : new JiboInteractionDecision(
                "memory_get_important_date",
                $"You told me your {label} is {storedDate}.");
    }

    private JiboInteractionDecision BuildRememberPreferenceDecision(TurnContext turn, string transcript)
    {
        var preference = TryExtractPreferenceSet(transcript);
        if (preference is null)
            return new JiboInteractionDecision(
                "memory_set_preference",
                "I can remember it if you say, my favorite music is jazz.");

        personalMemoryStore.SetPreference(ResolveTenantScope(turn), preference.Value.Category, preference.Value.Value);
        return new JiboInteractionDecision(
            "memory_set_preference",
            $"Got it. I will remember your favorite {preference.Value.Category} is {preference.Value.Value}.");
    }

    private JiboInteractionDecision BuildRecallPreferenceDecision(TurnContext turn, string transcript)
    {
        var category = TryExtractPreferenceLookupCategory(transcript);
        if (string.IsNullOrWhiteSpace(category))
            return new JiboInteractionDecision(
                "memory_get_preference",
                "Ask me like this: what is my favorite music?");

        var preference = personalMemoryStore.GetPreference(ResolveTenantScope(turn), category);
        return string.IsNullOrWhiteSpace(preference)
            ? new JiboInteractionDecision(
                "memory_get_preference",
                $"I do not know your favorite {category} yet.")
            : new JiboInteractionDecision(
                "memory_get_preference",
                $"You told me your favorite {category} is {preference}.");
    }

    private JiboInteractionDecision BuildRememberAffinityDecision(TurnContext turn, string transcript)
    {
        var affinitySet = TryExtractAffinitySet(transcript);
        if (affinitySet is null)
            return new JiboInteractionDecision(
                "memory_set_affinity",
                "I can remember it if you say, I like pizza or I dislike mushrooms.");

        personalMemoryStore.SetAffinity(ResolveTenantScope(turn), affinitySet.Value.Item, affinitySet.Value.Affinity);
        return new JiboInteractionDecision(
            "memory_set_affinity",
            $"Got it. I will remember you {DescribeAffinityAsVerb(affinitySet.Value.Affinity)} {affinitySet.Value.Item}.");
    }

    private JiboInteractionDecision BuildRecallAffinityDecision(TurnContext turn, string transcript)
    {
        var lookup = TryExtractAffinityLookup(transcript);
        if (lookup is null)
            return new JiboInteractionDecision(
                "memory_get_affinity",
                "Ask me like this: do I like pizza?");

        var affinity = personalMemoryStore.GetAffinity(ResolveTenantScope(turn), lookup.Value.Item);
        if (affinity is null)
            return new JiboInteractionDecision(
                "memory_get_affinity",
                $"I do not remember how you feel about {lookup.Value.Item} yet.");

        if (lookup.Value.ExpectedAffinity is null)
            return new JiboInteractionDecision(
                "memory_get_affinity",
                $"You told me you {DescribeAffinityAsVerb(affinity.Value)} {lookup.Value.Item}.");

        var matches = lookup.Value.ExpectedAffinity == PersonalAffinity.Dislike
            ? affinity == PersonalAffinity.Dislike
            : affinity is PersonalAffinity.Like or PersonalAffinity.Love;

        return matches
            ? new JiboInteractionDecision(
                "memory_get_affinity",
                $"Yes. You told me you {DescribeAffinityAsVerb(affinity.Value)} {lookup.Value.Item}.")
            : new JiboInteractionDecision(
                "memory_get_affinity",
                $"Not exactly. You told me you {DescribeAffinityAsVerb(affinity.Value)} {lookup.Value.Item}.");
    }
}