using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

internal enum YesNoTranscriptClassification
{
    None,
    Affirmative,
    Negative,
    Ambiguous
}

internal static class YesNoTranscriptClassifier
{
    private static readonly string[] YesNoAcknowledgementPrefixes =
    [
        "uh",
        "um",
        "hmm",
        "well",
        "so",
        "actually",
        "honestly",
        "thanks",
        "thank you"
    ];

    private static readonly HashSet<string> YesNoAffirmativeLeadTokens = new(StringComparer.Ordinal)
    {
        "yes",
        "yeah",
        "yep",
        "yup",
        "sure",
        "ok",
        "okay",
        "absolutely",
        "affirmative",
        "definitely",
        "certainly",
        "indeed"
    };

    private static readonly HashSet<string> YesNoNegativeLeadTokens = new(StringComparer.Ordinal)
    {
        "no",
        "nope",
        "nah",
        "negative",
        "never"
    };

    private static readonly HashSet<string> YesNoAffirmativeLeadPhrases = new(StringComparer.Ordinal)
    {
        "uh huh",
        "sounds good",
        "sure thing",
        "why not",
        "please do",
        "go ahead",
        "of course",
        "i guess so",
        "i think so"
    };

    private static readonly HashSet<string> YesNoNegativeLeadPhrases = new(StringComparer.Ordinal)
    {
        "not now",
        "not today",
        "not really",
        "no thanks",
        "no thank you",
        "maybe later",
        "i guess not",
        "i do not",
        "i dont",
        "i don t"
    };

    internal static YesNoTranscriptClassification Classify(string? transcript)
    {
        var normalized = Normalize(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return YesNoTranscriptClassification.None;

        while (TryTrimLeadingAcknowledgement(normalized, out var trimmed)) normalized = trimmed;
        if (string.IsNullOrWhiteSpace(normalized)) return YesNoTranscriptClassification.None;

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return YesNoTranscriptClassification.None;

        var selectedReply = YesNoTranscriptClassification.None;
        var selectedIndex = -1;
        var sawAffirmative = false;
        var sawNegative = false;

        for (var index = 0; index < tokens.Length; index += 1)
        {
            var token = tokens[index];
            if (YesNoNegativeLeadTokens.Contains(token))
            {
                Consider(YesNoTranscriptClassification.Negative, index);
                continue;
            }

            if (YesNoAffirmativeLeadTokens.Contains(token)) Consider(YesNoTranscriptClassification.Affirmative, index);
        }

        for (var index = 0; index + 1 < tokens.Length; index += 1)
        {
            var phrase = $"{tokens[index]} {tokens[index + 1]}";
            if (YesNoNegativeLeadPhrases.Contains(phrase))
            {
                Consider(YesNoTranscriptClassification.Negative, index + 1);
                continue;
            }

            if (YesNoAffirmativeLeadPhrases.Contains(phrase))
                Consider(YesNoTranscriptClassification.Affirmative, index + 1);
        }

        for (var index = 0; index + 2 < tokens.Length; index += 1)
        {
            var phrase = $"{tokens[index]} {tokens[index + 1]} {tokens[index + 2]}";
            if (YesNoNegativeLeadPhrases.Contains(phrase))
            {
                Consider(YesNoTranscriptClassification.Negative, index + 2);
                continue;
            }

            if (YesNoAffirmativeLeadPhrases.Contains(phrase))
                Consider(YesNoTranscriptClassification.Affirmative, index + 2);
        }

        return sawAffirmative && sawNegative
            ? YesNoTranscriptClassification.Ambiguous
            : selectedReply;

        void Consider(YesNoTranscriptClassification candidateReply, int candidateIndex)
        {
            if (candidateIndex < 0 || candidateIndex < selectedIndex) return;

            selectedReply = candidateReply;
            selectedIndex = candidateIndex;
            switch (candidateReply)
            {
                case YesNoTranscriptClassification.Affirmative:
                    sawAffirmative = true;
                    break;
                case YesNoTranscriptClassification.Negative:
                    sawNegative = true;
                    break;
            }
        }
    }

    internal static bool IsAffirmative(string? transcript)
    {
        return Classify(transcript) == YesNoTranscriptClassification.Affirmative;
    }

    internal static bool IsNegative(string? transcript)
    {
        return Classify(transcript) == YesNoTranscriptClassification.Negative;
    }

    private static bool TryTrimLeadingAcknowledgement(string normalizedTranscript, out string trimmedTranscript)
    {
        foreach (var acknowledgement in YesNoAcknowledgementPrefixes)
        {
            if (string.Equals(acknowledgement, "uh", StringComparison.Ordinal) &&
                (string.Equals(normalizedTranscript, "uh huh", StringComparison.Ordinal) ||
                 normalizedTranscript.StartsWith("uh huh ", StringComparison.Ordinal)))
                continue;

            if (string.Equals(normalizedTranscript, acknowledgement, StringComparison.Ordinal))
            {
                trimmedTranscript = string.Empty;
                return true;
            }

            if (!normalizedTranscript.StartsWith($"{acknowledgement} ", StringComparison.Ordinal)) continue;

            trimmedTranscript = normalizedTranscript[(acknowledgement.Length + 1)..].TrimStart();
            return true;
        }

        trimmedTranscript = normalizedTranscript;
        return false;
    }

    private static string Normalize(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return string.Empty;

        var normalized = Regex.Replace(transcript.Trim().ToLowerInvariant(), @"[^\p{L}\p{N}\s']+", " ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }
}