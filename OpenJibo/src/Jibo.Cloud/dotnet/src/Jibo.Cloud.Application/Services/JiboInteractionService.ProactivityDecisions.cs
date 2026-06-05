using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static readonly string[] PizzaPreferenceCategories =
    [
        "food",
        "meal",
        "dish",
        "dinner",
        "lunch",
        "snack"
    ];

    private JiboInteractionDecision BuildSurpriseDecision(
        JiboExperienceCatalog catalog,
        TurnContext turn,
        DateTimeOffset? referenceLocalTime)
    {
        var tenantScope = ResolveTenantScope(turn);
        var candidates = BuildProactivityCandidates(tenantScope, referenceLocalTime);
        if (candidates.Count == 0)
            return new JiboInteractionDecision("surprise", randomizer.Choose(catalog.SurpriseReplies));

        var highestWeight = candidates.Max(static candidate => candidate.Weight);
        var topCandidates = candidates
            .Where(candidate => candidate.Weight == highestWeight)
            .ToArray();
        var selected = topCandidates.Length == 1
            ? topCandidates[0]
            : randomizer.Choose(topCandidates);

        return selected.IntentName switch
        {
            "proactive_pizza_day" => BuildProactivePizzaDayDecision(referenceLocalTime),
            "proactive_pizza_preference" => BuildProactivePizzaPreferenceDecision(),
            "proactive_offer_pizza_fact" => BuildProactivePizzaFactOfferDecision(),
            "proactive_fun_fact" => BuildProactiveFunFactDecision(catalog),
            "proactive_joke" => BuildProactiveJokeDecision(catalog),
            _ => new JiboInteractionDecision("surprise", randomizer.Choose(catalog.SurpriseReplies))
        };
    }

    private List<ProactivityCandidate> BuildProactivityCandidates(
        PersonalMemoryTenantScope tenantScope,
        DateTimeOffset? referenceLocalTime)
    {
        var candidates = new List<ProactivityCandidate>();
        var referenceDate = (referenceLocalTime ?? DateTimeOffset.UtcNow).Date;

        var pizzaSignal = ResolvePizzaSignal(tenantScope);
        if (pizzaSignal.Affinity == PersonalAffinity.Dislike) return candidates;

        if (referenceDate is { Month: 2, Day: 9 })
        {
            var holidayWeight = pizzaSignal.Affinity switch
            {
                PersonalAffinity.Love => 170,
                PersonalAffinity.Like => 160,
                _ => 150
            };
            candidates.Add(new ProactivityCandidate("proactive_pizza_day", holidayWeight));
        }

        if (pizzaSignal.Affinity is PersonalAffinity.Love or PersonalAffinity.Like)
        {
            var preferenceWeight = pizzaSignal.Affinity == PersonalAffinity.Love ? 140 : 120;
            candidates.Add(new ProactivityCandidate("proactive_pizza_preference", preferenceWeight));
            candidates.Add(new ProactivityCandidate("proactive_offer_pizza_fact", preferenceWeight - 5));
            return candidates;
        }

        candidates.Add(new ProactivityCandidate("proactive_fun_fact", 90));
        candidates.Add(new ProactivityCandidate("proactive_joke", 90));
        candidates.Add(new ProactivityCandidate("proactive_offer_pizza_fact", 90));
        return candidates;
    }

    private PizzaSignal ResolvePizzaSignal(PersonalMemoryTenantScope tenantScope)
    {
        var pizzaAffinity = personalMemoryStore.GetAffinity(tenantScope, "pizza");
        if (pizzaAffinity is not null) return new PizzaSignal(pizzaAffinity);

        var affinityMatch = personalMemoryStore.GetAffinities(tenantScope)
            .Where(pair => pair.Key.Contains("pizza", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static pair =>
                pair.Value switch
                {
                    PersonalAffinity.Love => 2,
                    PersonalAffinity.Like => 1,
                    _ => 0
                })
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(affinityMatch.Key)) return new PizzaSignal(affinityMatch.Value);

        return PizzaPreferenceCategories.Select(category => personalMemoryStore.GetPreference(tenantScope, category))
            .Any(preference => !string.IsNullOrWhiteSpace(preference) &&
                               preference.Contains("pizza", StringComparison.OrdinalIgnoreCase))
            ? new PizzaSignal(PersonalAffinity.Like)
            : new PizzaSignal(null);
    }
}