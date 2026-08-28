using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private async Task<JiboInteractionDecision> BuildChatFallbackDecisionAsync(
        JiboExperienceCatalog catalog,
        string transcript,
        string lowered,
        string? chitchatEmotion,
        string? preferredName,
        CancellationToken cancellationToken)
    {
        var emotionDecision = ChitchatStateMachine.TryBuildChatEmotionDecision(
            lowered,
            catalog,
            randomizer,
            chitchatEmotion,
            preferredName);
        if (emotionDecision is not null) return emotionDecision;

        var isQuestion = TranscriptHeuristics.IsLikelyQuestion(transcript);
        if (!isQuestion)
            return ChitchatStateMachine.BuildNotUnderstoodDecision(transcript);

        var isWhoWhatLookup = WikipediaLookupParser.TryParse(transcript, out _);
        var skipKnowledgeSearch = SkillListenOwnership.IsNonQuestionKnowledgeQuery(lowered);
        var willSearch =
            !skipKnowledgeSearch &&
            (isWhoWhatLookup ||
             (knowledgeSearchService?.IsConfigured == true));

        if (willSearch)
            await EnsureSearchThinkingPreludeAsync(cancellationToken);

        var wikipediaLookup = skipKnowledgeSearch
            ? null
            : await TryLookupWikipediaAsync(transcript, cancellationToken);
        if (IsWikipediaFound(wikipediaLookup))
            return BuildWikipediaFoundDecision(wikipediaLookup!);

        var searchDecision = skipKnowledgeSearch
            ? null
            : await TryBuildKnowledgeSearchDecisionAsync(transcript, cancellationToken);
        if (IsKnowledgeSearchFound(searchDecision))
            return searchDecision!;

        if (isWhoWhatLookup)
        {
            var wikipediaRecheck = await TryLookupWikipediaAsync(
                transcript,
                cancellationToken,
                bypassCache: true);
            if (IsWikipediaFound(wikipediaRecheck))
                return BuildWikipediaFoundDecision(wikipediaRecheck!);

            if (IsKnowledgeSearchUnavailable(searchDecision) ||
                wikipediaRecheck?.Outcome == WikipediaSummaryOutcome.Unavailable ||
                (searchDecision is null && wikipediaLookup?.Outcome == WikipediaSummaryOutcome.Unavailable))
                return ChitchatStateMachine.BuildKnowledgeSearchUnavailableDecision();

            return ChitchatStateMachine.BuildKnowledgeSearchNotFoundDecision(transcript);
        }

        if (searchDecision is not null)
            return searchDecision;

        return ChitchatStateMachine.BuildChatErrorResponseDecision(
            BuildGenericReply(catalog, transcript, lowered),
            transcript);
    }

    private async Task EnsureSearchThinkingPreludeAsync(CancellationToken cancellationToken)
    {
        if (turnProgressPublisher is null) return;

        // Flush EOS + non-final LISTEN (skillID=answer) so the robot remaps to Nimbus and
        // starts Thinking_Eye_Loop_01, then begin HTTP search. Never send an interim SKILL_ACTION.
        await turnProgressPublisher.PublishSearchThinkingPreludeAsync(cancellationToken);
    }

    private async Task<WikipediaSummaryResult?> TryLookupWikipediaAsync(
        string transcript,
        CancellationToken cancellationToken,
        bool bypassCache = false)
    {
        if (wikipediaSummaryProvider is null) return null;
        if (!WikipediaLookupParser.TryParse(transcript, out var subject) ||
            string.IsNullOrWhiteSpace(subject))
            return null;

        try
        {
            return await wikipediaSummaryProvider.GetSummaryAsync(subject, cancellationToken, bypassCache);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return WikipediaSummaryResult.Unavailable();
        }
    }

    private async Task<JiboInteractionDecision?> TryBuildKnowledgeSearchDecisionAsync(
        string transcript,
        CancellationToken cancellationToken)
    {
        if (knowledgeSearchService is null || !knowledgeSearchService.IsConfigured)
            return null;

        var query = NormalizeCommandPhrase(transcript).Trim();
        if (string.IsNullOrWhiteSpace(query)) return null;
        if (SkillListenOwnership.IsNonQuestionKnowledgeQuery(query)) return null;

        KnowledgeSearchResult? result;
        try
        {
            result = await knowledgeSearchService.SearchAsync(query, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ChitchatStateMachine.BuildKnowledgeSearchUnavailableDecision();
        }

        if (result is null)
            return ChitchatStateMachine.BuildKnowledgeSearchUnavailableDecision();

        return result.Outcome switch
        {
            KnowledgeSearchOutcome.Found when !string.IsNullOrWhiteSpace(result.AnswerText) =>
                ChitchatStateMachine.BuildKnowledgeSearchResponseDecision(
                    KnowledgeSearchSpokenReplyFormatter.FormatReply(result.AnswerText, result.BackendKind)),
            KnowledgeSearchOutcome.Unavailable =>
                ChitchatStateMachine.BuildKnowledgeSearchUnavailableDecision(),
            _ => ChitchatStateMachine.BuildKnowledgeSearchNotFoundDecision(transcript)
        };
    }

    private static bool IsWikipediaFound(WikipediaSummaryResult? result) =>
        result?.Outcome == WikipediaSummaryOutcome.Found &&
        !string.IsNullOrWhiteSpace(result.Summary);

    private static JiboInteractionDecision BuildWikipediaFoundDecision(WikipediaSummaryResult result) =>
        ChitchatStateMachine.BuildKnowledgeSearchResponseDecision(
            KnowledgeSearchSpokenReplyFormatter.FormatReply(result.Summary!, SearchBackendKind.Wikipedia));

    private static bool IsKnowledgeSearchFound(JiboInteractionDecision? decision) =>
        decision is not null &&
        string.Equals(decision.IntentName, "knowledge_search", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnowledgeSearchUnavailable(JiboInteractionDecision? decision) =>
        decision is not null &&
        string.Equals(decision.IntentName, "knowledge_search_unavailable", StringComparison.OrdinalIgnoreCase);
}
