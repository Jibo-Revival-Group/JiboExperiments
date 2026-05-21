using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static JiboInteractionDecision BuildNewsDecision(
        string spokenBriefing,
        string? sourceName,
        IReadOnlyList<string>? categories,
        int? headlineCount,
        IReadOnlyDictionary<string, object?>? providerDiagnostics = null)
    {
        var speakableBriefing = NormalizeNewsSpeechText(spokenBriefing);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["skillId"] = "news",
            ["cloudSkill"] = "news",
            ["mim_id"] = "runtime-news",
            ["mim_type"] = "announcement",
            ["prompt_id"] = "NewsHeadline_AN_01",
            ["prompt_sub_category"] = "AN",
            ["esml"] =
                $"<speak><anim cat='news' meta='news-stinger' nonBlocking='true' /><break size='0.35'/><es cat='neutral' filter='!ssa-only, !sfx-only' endNeutral='true'>{EscapeForEsml(speakableBriefing)}</es></speak>"
        };

        if (!string.IsNullOrWhiteSpace(sourceName)) payload["news_source"] = sourceName;

        if (headlineCount is > 0) payload["news_headline_count"] = headlineCount.Value;

        if (categories is { Count: > 0 }) payload["news_categories"] = categories.ToArray();

        if (providerDiagnostics is not null)
            foreach (var (key, value) in providerDiagnostics)
                payload[key] = value;

        return new JiboInteractionDecision("news", spokenBriefing, "news", payload);
    }

    private static JiboInteractionDecision BuildProviderNewsDecision(
        NewsBriefingSnapshot snapshot,
        JiboExperienceCatalog catalog,
        IReadOnlyList<string> preferredCategories,
        int requestedHeadlineCount)
    {
        var headlines = snapshot.Headlines
            .Where(headline => !string.IsNullOrWhiteSpace(headline.Title))
            .Take(MaxNewsHeadlines)
            .ToArray();
        if (headlines.Length == 0)
            return BuildNewsDecision(
                "I couldn't load fresh headlines right now.",
                snapshot.SourceName,
                preferredCategories,
                0,
                BuildNewsProviderDiagnostics(
                    "provider_empty",
                    preferredCategories,
                    requestedHeadlineCount,
                    0));

        var leadIn = BuildNewsLeadIn(snapshot.SourceName, preferredCategories);
        var joinedHeadlines = string.Join(" ", headlines.Select(static headline => $"{headline.Title}."));
        var outroTemplate = ChooseShortestTemplate(catalog.NewsOutroReplies) ?? "And that's the news.";
        var spokenBriefing = $"{leadIn} {joinedHeadlines} {outroTemplate}".Trim();
        return BuildNewsDecision(
            spokenBriefing,
            snapshot.SourceName,
            preferredCategories,
            headlines.Length,
            BuildNewsProviderDiagnostics(
                "provider_success",
                preferredCategories,
                requestedHeadlineCount,
                headlines.Length));
    }

    private static IReadOnlyDictionary<string, object?> BuildNewsProviderDiagnostics(
        string status,
        IReadOnlyList<string> preferredCategories,
        int requestedHeadlineCount,
        int? resolvedHeadlineCount = null,
        string? providerMessage = null,
        int? providerHttpStatusCode = null,
        string? providerEndpoint = null,
        string? providerErrorCode = null)
    {
        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["news_provider_status"] = status,
            ["news_provider_requested_headlines"] = requestedHeadlineCount,
            ["news_provider_preferred_categories"] = preferredCategories.Count > 0
                ? [.. preferredCategories]
                : Array.Empty<string>()
        };

        if (resolvedHeadlineCount is not null)
            diagnostics["news_provider_resolved_headlines"] = resolvedHeadlineCount.Value;

        if (!string.IsNullOrWhiteSpace(providerMessage)) diagnostics["news_provider_message"] = providerMessage;

        if (providerHttpStatusCode is not null) diagnostics["news_provider_http_status"] = providerHttpStatusCode.Value;

        if (!string.IsNullOrWhiteSpace(providerEndpoint)) diagnostics["news_provider_endpoint"] = providerEndpoint;

        if (!string.IsNullOrWhiteSpace(providerErrorCode)) diagnostics["news_provider_error_code"] = providerErrorCode;

        return diagnostics;
    }

    private static string ResolveNewsProviderStatus(NewsBriefingSnapshot? snapshot)
    {
        var providerStatus = snapshot?.ProviderStatus?.Trim().ToLowerInvariant();
        return providerStatus switch
        {
            "success" => "provider_success",
            "exception" => "provider_exception",
            "http_error" or "api_error" or "schema_error" => "provider_error",
            _ => "provider_empty"
        };
    }

    private static string BuildNewsLeadIn(string? sourceName, IReadOnlyList<string> preferredCategories)
    {
        var categoryLeadIn = preferredCategories.Count switch
        {
            <= 0 => "Here are a few headlines.",
            1 => $"Here are your {preferredCategories[0]} headlines.",
            _ => $"Here are your {preferredCategories[0]} and {preferredCategories[1]} headlines."
        };

        return string.IsNullOrWhiteSpace(sourceName)
            ? categoryLeadIn
            : $"{categoryLeadIn} Source: {sourceName}.";
    }

    private static string NormalizeNewsSpeechText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Expand "AI" so Nimbus TTS does not collapse it to a single "aye" sound.
        var normalized = Regex.Replace(
            text,
            @"\bA\.?\s*I\.?\b",
            "artificial intelligence",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return NormalizeLocationForSpeech(normalized);
    }

    private List<string> ResolvePreferredNewsCategories(TurnContext turn, string transcript)
    {
        var categories = new List<string>();
        var normalizedTranscript = NormalizeCommandPhrase(transcript);

        foreach (var (keyword, category) in NewsCategoryKeywordMap)
            if (normalizedTranscript.Contains(keyword, StringComparison.Ordinal))
                AddNewsCategory(categories, category);

        var tenantScope = ResolveTenantScope(turn);
        var explicitPreference = personalMemoryStore.GetPreference(tenantScope, "news");
        if (!string.IsNullOrWhiteSpace(explicitPreference))
            foreach (var category in MapNewsCategoryText(explicitPreference))
                AddNewsCategory(categories, category);

        foreach (var (item, affinity) in personalMemoryStore.GetAffinities(tenantScope))
        {
            if (affinity == PersonalAffinity.Dislike) continue;

            foreach (var category in MapNewsCategoryText(item)) AddNewsCategory(categories, category);
        }

        return [.. categories.Take(MaxPreferredNewsCategories)];
    }

    private static IEnumerable<string> MapNewsCategoryText(string text)
    {
        var normalized = NormalizeCommandPhrase(text);
        if (string.IsNullOrWhiteSpace(normalized)) yield break;

        foreach (var (keyword, category) in NewsCategoryKeywordMap)
            if (normalized.Contains(keyword, StringComparison.Ordinal))
                yield return category;
    }

    private static void AddNewsCategory(ICollection<string> categories, string category)
    {
        if (categories.Contains(category, StringComparer.OrdinalIgnoreCase)) return;

        categories.Add(category);
    }
}
