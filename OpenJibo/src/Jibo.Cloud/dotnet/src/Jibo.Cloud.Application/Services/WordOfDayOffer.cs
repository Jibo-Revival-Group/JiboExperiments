namespace Jibo.Cloud.Application.Services;

internal static class WordOfDayOffer
{
    public const string SurpriseRule = "word-of-the-day/surprise";
    public const string OfferRule = "word-of-the-day/offer";
    public const string TutorialIntent = "word_of_the_day_offer_tutorial";

    public static bool IsSurpriseRule(string? rule) =>
        string.Equals(rule, SurpriseRule, StringComparison.OrdinalIgnoreCase);

    public static bool IsYesNoRule(string? rule) =>
        IsSurpriseRule(rule) || string.Equals(rule, OfferRule, StringComparison.OrdinalIgnoreCase);

    public static bool IsTutorialReply(string transcript) =>
        TranscriptTextNormalizer.NormalizeLooseText(transcript.Replace('\'', ' ')) is
            "whats that" or "what s that" or "what is that" or "what was that" or
            "play what" or "playing what" or "say what" or "word of the day" or "word of the day game" or
            "whats word of the day" or "what s word of the day" or "what is word of the day" or
            "whats word of the day game" or "what s word of the day game" or "what is word of the day game";
}
