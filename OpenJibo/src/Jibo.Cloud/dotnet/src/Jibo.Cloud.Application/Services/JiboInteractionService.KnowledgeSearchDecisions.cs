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

        var searchDecision = await TryBuildKnowledgeSearchDecisionAsync(transcript, cancellationToken);
        if (searchDecision is not null) return searchDecision;

        return ChitchatStateMachine.BuildChatErrorResponseDecision(
            BuildGenericReply(catalog, transcript, lowered),
            transcript);
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
            return null;
        }

        if (result is null || string.IsNullOrWhiteSpace(result.AnswerText)) return null;

        return ChitchatStateMachine.BuildKnowledgeSearchResponseDecision(
            KnowledgeSearchSpokenReplyFormatter.FormatReply(result.AnswerText, result.BackendKind));
    }
}