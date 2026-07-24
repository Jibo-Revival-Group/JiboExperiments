namespace Jibo.Cloud.Application.Services;

internal static class HomeAssistantEntityNameMatcher
{
    public static HomeAssistantCommandCandidate? FindClosest(
        string heard,
        IReadOnlyList<HomeAssistantCommandCandidate> candidates)
    {
        var normalizedTarget = Normalize(heard);
        if (string.IsNullOrWhiteSpace(normalizedTarget) || candidates.Count == 0)
            return null;

        HomeAssistantCommandCandidate? exact = null;
        HomeAssistantCommandCandidate? partial = null;
        (int Distance, double NegSimilarity, HomeAssistantCommandCandidate Candidate)? bestFuzzy = null;

        foreach (var candidate in candidates)
        {
            var normalizedCandidate = Normalize(candidate.Name);
            if (string.IsNullOrWhiteSpace(normalizedCandidate)) continue;

            if (string.Equals(normalizedCandidate, normalizedTarget, StringComparison.Ordinal))
            {
                exact = candidate;
                break;
            }

            if ((normalizedTarget.Contains(normalizedCandidate, StringComparison.Ordinal) ||
                 normalizedCandidate.Contains(normalizedTarget, StringComparison.Ordinal)) &&
                partial is null)
                partial = candidate;

            var distance = ComputeEditDistance(normalizedTarget, normalizedCandidate);
            var maxLen = Math.Max(Math.Max(normalizedTarget.Length, normalizedCandidate.Length), 1);
            var similarity = 1.0 - (distance / (double)maxLen);
            var threshold = Math.Max(2, normalizedTarget.Length / 3);
            if (distance <= threshold || similarity >= 0.55)
            {
                var ranked = (distance, -similarity, candidate);
                if (bestFuzzy is null ||
                    ranked.distance < bestFuzzy.Value.Distance ||
                    (ranked.distance == bestFuzzy.Value.Distance &&
                     ranked.Item2 < bestFuzzy.Value.NegSimilarity))
                    bestFuzzy = ranked;
            }
        }

        return exact ?? partial ?? bestFuzzy?.Candidate;
    }

    public static string FormatCandidateList(IReadOnlyList<HomeAssistantCommandCandidate> candidates)
    {
        var names = candidates
            .Select(candidate => candidate.Name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return names.Length switch
        {
            0 => "one of them",
            1 => names[0],
            2 => $"{names[0]} or {names[1]}",
            _ => string.Join(", ", names.Take(names.Length - 1)) + $", or {names[^1]}"
        };
    }

    private static string Normalize(string value)
    {
        var normalized = TranscriptTextNormalizer.NormalizeLooseText(value);
        foreach (var suffix in new[] { " thermostat", " hvac", " heat", " ac", " light", " lights", " lamp", " lamps" })
            if (normalized.EndsWith(suffix, StringComparison.Ordinal))
                normalized = normalized[..^suffix.Length].Trim();
        return normalized;
    }

    private static int ComputeEditDistance(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal)) return 0;
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 0; i < left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i + 1;
            for (var j = 0; j < right.Length; j++)
            {
                var cost = left[i] == right[j] ? 0 : 1;
                current[j + 1] = Math.Min(
                    Math.Min(current[j] + 1, previous[j + 1] + 1),
                    previous[j] + cost);
            }

            previous = current;
        }

        return previous[^1];
    }
}
