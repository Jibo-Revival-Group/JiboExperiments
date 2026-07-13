namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private JiboInteractionDecision BuildRollDiceDecision(string transcript)
    {
        if (!RollDiceCommandParser.TryParse(transcript, out var query) ||
            !RollDiceCommandParser.IsValidSideCount(query.Sides))
        {
            return new JiboInteractionDecision(
                "roll_dice",
                DiceRollSpokenReplyFormatter.FormatInvalidSides());
        }

        var result = DiceRoller.Roll(randomizer, query.Sides);
        var replyText = DiceRollSpokenReplyFormatter.Format(query.Sides, result);
        var esml = DiceRollSpokenReplyFormatter.FormatEsml(query.Sides, result, replyText);
        if (esml is null)
            return new JiboInteractionDecision("roll_dice", replyText);

        return new JiboInteractionDecision(
            "roll_dice",
            replyText,
            "chitchat-skill",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["esml"] = esml,
                ["mim_id"] = "RA_JBO_RollOneDie",
                ["mim_type"] = "announcement",
                ["prompt_id"] = "RA_JBO_RollOneDie_AN_01",
                ["prompt_sub_category"] = "AN"
            });
    }
}
