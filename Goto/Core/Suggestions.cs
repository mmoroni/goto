namespace Goto.Core;

internal static class Suggestions
{
    private const int MaxSuggestions = 3;

    /// <summary>
    /// Given a TooMany candidate set and the original leaf search term, computes ancestor
    /// folder names that appear in SOME candidates but not ALL of them -- names that would
    /// narrow the set if added as a qualifier -- and formats them as ready-to-run commands.
    /// A heuristic hint, not a precise algorithm.
    /// </summary>
    public static IReadOnlyList<string> Suggest(IReadOnlyList<string> candidates, string leafTerm)
    {
        if (candidates.Count < 2)
            return [];

        var ancestorToCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in candidates)
        {
            var names = new HashSet<string>(Matcher.GetAncestorNames(candidate), StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
                ancestorToCount[name] = ancestorToCount.GetValueOrDefault(name) + 1;
        }

        int total = candidates.Count;

        // "Splits the set" = appears in some but not all candidates.
        var splitters = ancestorToCount.Where(kv => kv.Value > 0 && kv.Value < total);

        // Order: most-evenly-splitting first (closest to total/2), then most frequent,
        // then alphabetical as a final tiebreaker.
        return splitters
            .OrderBy(kv => Math.Abs(kv.Value - total / 2.0))
            .ThenByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)
            .Select(kv => $"goto {kv.Key} {leafTerm}")
            .ToArray();
    }
}
