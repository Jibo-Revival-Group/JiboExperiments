using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static readonly Regex NewsCorrectionPrefixPattern = new(
        @"^\s*(?:correction|corrected|update|updated)\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NewsUnsafeTermPattern = new(
        @"\b(?:murder|homicide|suicide|porn|pornography|sex\s+crime|sexual\s+assault|graphic\s+violence|beheading|massacre)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static JiboInteractionDecision BuildNewsDecision(
        string spokenBriefing,
        string? sourceName,
        IReadOnlyList<string>? categories,
        int? headlineCount,
        IReadOnlyDictionary<string, object?>? providerDiagnostics = null,
        IReadOnlyList<NewsHeadline>? headlines = null)
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
            ["news_view_enabled"] = true,
            ["news_view_kind"] = "newsBriefing",
            ["news_view_mode"] = "provider",
            ["esml"] =
                $"<speak><anim cat='news' meta='news-stinger' nonBlocking='true' /><break size='0.35'/><es cat='neutral' filter='!ssa-only, !sfx-only' endNeutral='true'>{EscapeForEsml(speakableBriefing)}</es></speak>"
        };

        if (!string.IsNullOrWhiteSpace(sourceName)) payload["news_source"] = sourceName;

        if (headlineCount is > 0) payload["news_headline_count"] = headlineCount.Value;

        if (categories is { Count: > 0 }) payload["news_categories"] = categories.ToArray();

        if (headlines is { Count: > 0 })
            payload["news_headlines"] = headlines.Select(static headline => new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = headline.Title,
                    ["summary"] = headline.Summary,
                    ["category"] = headline.Category,
                    ["sourceName"] = headline.SourceName,
                    ["url"] = headline.Url
                })
                .ToArray();

        if (providerDiagnostics is null) return new JiboInteractionDecision("news", spokenBriefing, "news", payload);

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
        var filteredHeadlines = FilterNewsHeadlinesForJibo(snapshot.Headlines);
        var headlines = filteredHeadlines.Headlines
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
                    0,
                    skippedHeadlineCount: filteredHeadlines.SkippedCount));

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
                headlines.Length,
                skippedHeadlineCount: filteredHeadlines.SkippedCount),
            headlines);
    }

    private static FilteredNewsHeadlines FilterNewsHeadlinesForJibo(IReadOnlyList<NewsHeadline> sourceHeadlines)
    {
        var accepted = new List<NewsHeadline>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;

        foreach (var headline in sourceHeadlines)
        {
            var title = NormalizeNewsHeadlineField(headline.Title);
            var summary = NormalizeNewsHeadlineField(headline.Summary);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(summary))
            {
                skipped++;
                continue;
            }

            if (!IsJiboSafeNewsHeadline(title, summary))
            {
                skipped++;
                continue;
            }

            var duplicateKey = Regex.Replace(title, @"\s+", " ").Trim();
            if (!seenTitles.Add(duplicateKey))
            {
                skipped++;
                continue;
            }

            accepted.Add(headline with { Title = title, Summary = summary });
        }

        return new FilteredNewsHeadlines(accepted, skipped);
    }

    private static string? NormalizeNewsHeadlineField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static bool IsJiboSafeNewsHeadline(string title, string summary)
    {
        if (NewsCorrectionPrefixPattern.IsMatch(title)) return false;
        return !NewsUnsafeTermPattern.IsMatch(title) && !NewsUnsafeTermPattern.IsMatch(summary);
    }

    private static IReadOnlyDictionary<string, object?> BuildNewsProviderDiagnostics(
        string status,
        IReadOnlyList<string> preferredCategories,
        int requestedHeadlineCount,
        int? resolvedHeadlineCount = null,
        string? providerMessage = null,
        int? providerHttpStatusCode = null,
        string? providerEndpoint = null,
        string? providerErrorCode = null,
        int? skippedHeadlineCount = null)
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

        if (skippedHeadlineCount is > 0) diagnostics["news_provider_skipped_headlines"] = skippedHeadlineCount.Value;

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

    private sealed record FilteredNewsHeadlines(IReadOnlyList<NewsHeadline> Headlines, int SkippedCount);
}