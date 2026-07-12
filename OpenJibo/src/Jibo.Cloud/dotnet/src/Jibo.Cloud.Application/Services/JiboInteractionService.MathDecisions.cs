namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private JiboInteractionDecision BuildMathDecision(string transcript)
    {
        MathCommandParser.TryParse(transcript, out var query);
        var evaluation = MathCommandParser.Evaluate(query);
        var reply = MathSpokenReplyFormatter.Format(query, evaluation, randomizer);
        return new JiboInteractionDecision("math_query", reply);
    }
}
