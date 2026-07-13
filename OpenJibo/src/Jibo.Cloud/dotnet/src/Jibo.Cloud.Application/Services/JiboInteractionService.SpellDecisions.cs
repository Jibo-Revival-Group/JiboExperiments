namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static JiboInteractionDecision BuildSpellDecision(string transcript)
    {
        SpellCommandParser.TryParse(transcript, out var word);
        var reply = SpellSpokenReplyFormatter.Format(word);
        return new JiboInteractionDecision("spell_word", reply);
    }
}
