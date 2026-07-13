namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private async Task<JiboInteractionDecision> BuildDefineWordDecisionAsync(
        string transcript,
        CancellationToken cancellationToken)
    {
        DefineCommandParser.TryParse(transcript, out var word);
        if (string.IsNullOrWhiteSpace(word))
            return new JiboInteractionDecision("define_word", DefinitionSpokenReplyFormatter.FormatMissingWord());

        string? definition = null;
        if (wordDefinitionProvider is not null)
        {
            try
            {
                definition = await wordDefinitionProvider.GetDefinitionAsync(word, cancellationToken);
            }
            catch
            {
                // Fall through to the not-found reply.
            }
        }

        return new JiboInteractionDecision(
            "define_word",
            DefinitionSpokenReplyFormatter.Format(definition));
    }
}
