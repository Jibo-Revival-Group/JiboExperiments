using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

internal static class WordOfDayGuessResolver
{
    private static readonly Regex PunctuationToSpaceRegex = new(
        @"[^\p{L}\p{N}\s']+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static string Resolve(string transcript, IReadOnlyList<string> hints, string? explicitGuess = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitGuess)) return explicitGuess;

        var normalized = NormalizeGuessToken(transcript);
        var hintIndex = normalized switch
        {
            "1" or "one" or "first" => 0,
            "2" or "two" or "second" => 1,
            "3" or "three" or "third" => 2,
            _ => -1
        };

        if (hintIndex >= 0)
            return hintIndex < hints.Count
                ? hints[hintIndex]
                : transcript;

        var fuzzyHintMatch = FindClosestHint(normalized, hints);
        return string.IsNullOrWhiteSpace(fuzzyHintMatch)
            ? transcript
            : fuzzyHintMatch;
    }

    private static string? FindClosestHint(string normalizedTranscript, IReadOnlyList<string> hints)
    {
        if (string.IsNullOrWhiteSpace(normalizedTranscript)) return null;

        var candidates = BuildGuessCandidates(normalizedTranscript).ToArray();
        if (candidates.Length == 0) return null;

        var normalizedHints = hints
            .Select(hint => new HintCandidate(hint, NormalizeGuessToken(hint)))
            .Where(static hint => !string.IsNullOrWhiteSpace(hint.Normalized))
            .ToArray();
        if (normalizedHints.Length == 0) return null;

        foreach (var hint in normalizedHints)
            if (candidates.Any(candidate => string.Equals(candidate, hint.Normalized, StringComparison.Ordinal)))
                return hint.Original;

        string? bestHint = null;
        var bestDistance = int.MaxValue;
        var bestMatchCount = 0;

        foreach (var hint in normalizedHints)
        {
            var distance = candidates.Min(candidate => ComputeEditDistance(candidate, hint.Normalized));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestHint = hint.Original;
                bestMatchCount = 1;
            }
            else if (distance == bestDistance)
            {
                bestMatchCount += 1;
            }
        }

        if (bestDistance <= 2 && bestMatchCount == 1) return bestHint;

        var prefixHintMatch = FindSinglePrefixHintMatch(candidates, normalizedHints);
        return !string.IsNullOrWhiteSpace(prefixHintMatch)
            ? prefixHintMatch
            : FindSinglePhoneticHintMatch(candidates, normalizedHints);
    }

    private static string? FindSinglePrefixHintMatch(
        IReadOnlyList<string> candidates,
        IReadOnlyList<HintCandidate> hints)
    {
        string? bestHint = null;
        var bestPrefixLength = 0;
        var matchCount = 0;

        foreach (var candidate in candidates.Where(static candidate => candidate.Length >= 4))
        foreach (var hint in hints.Where(static hint => hint.Normalized.Length >= 5))
        {
            var comparedLength = Math.Min(candidate.Length, hint.Normalized.Length);
            var hintPrefix = hint.Normalized[..comparedLength];
            if (ComputeEditDistance(candidate, hintPrefix) > 1) continue;

            var prefixLength = CommonPrefixLength(candidate, hint.Normalized);
            if (prefixLength < 3) continue;

            if (prefixLength > bestPrefixLength)
            {
                bestHint = hint.Original;
                bestPrefixLength = prefixLength;
                matchCount = 1;
            }
            else if (prefixLength == bestPrefixLength &&
                     !string.Equals(bestHint, hint.Original, StringComparison.Ordinal))
            {
                matchCount += 1;
            }
        }

        return matchCount == 1 ? bestHint : null;
    }

    private static string? FindSinglePhoneticHintMatch(
        IReadOnlyList<string> candidates,
        IReadOnlyList<HintCandidate> hints)
    {
        string? bestHint = null;
        var bestDistance = int.MaxValue;
        var matchCount = 0;

        foreach (var candidate in candidates.Where(static candidate => candidate.Length >= 5))
        {
            var candidateSoundex = ComputeSoundex(candidate);
            if (string.IsNullOrWhiteSpace(candidateSoundex)) continue;

            foreach (var hint in hints.Where(static hint => hint.Normalized.Length >= 5))
            {
                if (!string.Equals(candidateSoundex, ComputeSoundex(hint.Normalized), StringComparison.Ordinal))
                    continue;

                var distance = ComputeEditDistance(candidate, hint.Normalized);
                if (distance < bestDistance)
                {
                    bestHint = hint.Original;
                    bestDistance = distance;
                    matchCount = 1;
                }
                else if (distance == bestDistance && !string.Equals(bestHint, hint.Original, StringComparison.Ordinal))
                {
                    matchCount += 1;
                }
            }
        }

        return matchCount == 1 ? bestHint : null;
    }

    private static IEnumerable<string> BuildGuessCandidates(string normalizedTranscript)
    {
        yield return normalizedTranscript;

        foreach (var token in normalizedTranscript.Split(' ',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return token.Trim('\'');
    }

    private static string NormalizeGuessToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return WhitespaceRegex.Replace(
                PunctuationToSpaceRegex.Replace(value.Trim().ToLowerInvariant(), " "),
                " ")
            .Trim();
    }

    private static string ComputeSoundex(string value)
    {
        var letters = value
            .Where(char.IsLetter)
            .Select(char.ToUpperInvariant)
            .ToArray();
        if (letters.Length == 0) return string.Empty;

        var firstLetter = letters[0];
        var previousCode = EncodeSoundexLetter(firstLetter);
        var codes = new List<char>(3);

        foreach (var letter in letters.Skip(1))
        {
            var code = EncodeSoundexLetter(letter);
            if (code != '0' && code != previousCode) codes.Add(code);
            previousCode = code;
        }

        return $"{firstLetter}{new string(codes.Take(3).ToArray())}".PadRight(4, '0');
    }

    private static char EncodeSoundexLetter(char value)
    {
        return value switch
        {
            'B' or 'F' or 'P' or 'V' => '1',
            'C' or 'G' or 'J' or 'K' or 'Q' or 'S' or 'X' or 'Z' => '2',
            'D' or 'T' => '3',
            'L' => '4',
            'M' or 'N' => '5',
            'R' => '6',
            _ => '0'
        };
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < length && left[index] == right[index]) index += 1;
        return index;
    }

    private static int ComputeEditDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column += 1) previous[column] = column;

        for (var row = 1; row <= left.Length; row += 1)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column += 1)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private sealed record HintCandidate(string Original, string Normalized);
}