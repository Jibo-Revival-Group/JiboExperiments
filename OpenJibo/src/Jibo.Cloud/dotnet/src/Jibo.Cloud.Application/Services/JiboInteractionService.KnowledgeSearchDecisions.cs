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

        var wikipediaLookup = await TryLookupWikipediaAsync(transcript, cancellationToken);
        if (wikipediaLookup?.Outcome == WikipediaSummaryOutcome.Found &&
            !string.IsNullOrWhiteSpace(wikipediaLookup.Summary))
        {
            return ChitchatStateMachine.BuildKnowledgeSearchResponseDecision(
                KnowledgeSearchSpokenReplyFormatter.FormatReply(
                    wikipediaLookup.Summary,
                    SearchBackendKind.Wikipedia));
        }

        var searchDecision = await TryBuildKnowledgeSearchDecisionAsync(transcript, cancellationToken);
        if (searchDecision is not null) return searchDecision;

        if (WikipediaLookupParser.TryParse(transcript, out _))
        {
            if (wikipediaLookup?.Outcome == WikipediaSummaryOutcome.Unavailable)
                return ChitchatStateMachine.BuildKnowledgeSearchUnavailableDecision();

            return ChitchatStateMachine.BuildKnowledgeSearchNotFoundDecision(transcript);
        }

        return ChitchatStateMachine.BuildChatErrorResponseDecision(
            BuildGenericReply(catalog, transcript, lowered),
            transcript);
    }

    private async Task<WikipediaSummaryResult?> TryLookupWikipediaAsync(
        string transcript,
        CancellationToken cancellationToken)
    {
        if (wikipediaSummaryProvider is null) return null;
        if (!WikipediaLookupParser.TryParse(transcript, out var subject) ||
            string.IsNullOrWhiteSpace(subject))
            return null;

        try
        {
            return await wikipediaSummaryProvider.GetSummaryAsync(subject, cancellationToken);
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
}
