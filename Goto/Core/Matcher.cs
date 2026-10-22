using Goto.Search;

namespace Goto.Core;

internal static class Matcher
{
    public static bool IsMatch(string candidatePath, GotoQuery query, MatchMode mode)
    {
        string leaf = Path.GetFileName(candidatePath);
        if (string.IsNullOrEmpty(leaf) || !LeafMatches(leaf, query.Leaf, mode))
            return false;

        if (query.Qualifiers.Count == 0)
            return true;

        List<string> ancestors = GetAncestorNames(candidatePath); // root-to-leaf order, leaf excluded

        int searchFrom = 0;
        foreach (string qualifier in query.Qualifiers)
        {
            int foundAt = -1;
            for (int i = searchFrom; i < ancestors.Count; i++)
            {
                if (ancestors[i].Contains(qualifier, StringComparison.OrdinalIgnoreCase))
                {
                    foundAt = i;
                    break;
                }
            }

            if (foundAt < 0)
                return false;

            searchFrom = foundAt + 1; // monotonic: later qualifiers can't reuse an earlier ancestor
        }

        return true;
    }

    internal static bool LeafMatches(string leaf, string term, MatchMode mode) => mode switch
    {
        MatchMode.Exact => string.Equals(leaf, term, StringComparison.OrdinalIgnoreCase),
        MatchMode.Prefix => leaf.StartsWith(term, StringComparison.OrdinalIgnoreCase),
        MatchMode.Contains => leaf.Contains(term, StringComparison.OrdinalIgnoreCase),
        MatchMode.Abbreviation => IsAbbreviation(leaf, term),
        _ => false,
    };

    /// <summary>
    /// T4: first-char-anchored wildcard match (spec's "d*o*c*" notation). The term's first
    /// character must equal the leaf's first character; every remaining term character must
    /// then appear, in order, as a (not necessarily contiguous) subsequence of the rest of
    /// the leaf.
    /// </summary>
    private static bool IsAbbreviation(string leaf, string term)
    {
        if (term.Length == 0 || leaf.Length == 0)
            return false;

        if (char.ToLowerInvariant(term[0]) != char.ToLowerInvariant(leaf[0]))
            return false;

        int leafIdx = 1;
        for (int termIdx = 1; termIdx < term.Length; termIdx++)
        {
            char want = char.ToLowerInvariant(term[termIdx]);
            bool found = false;
            while (leafIdx < leaf.Length)
            {
                bool isMatch = char.ToLowerInvariant(leaf[leafIdx]) == want;
                leafIdx++;
                if (isMatch)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                return false;
        }

        return true;
    }

    internal static List<string> GetAncestorNames(string candidatePath)
    {
        var ancestors = new List<string>();
        string? parent = Path.GetDirectoryName(candidatePath);

        while (!string.IsNullOrEmpty(parent))
        {
            string name = Path.GetFileName(parent);
            if (!string.IsNullOrEmpty(name))
                ancestors.Add(name); // "" for a bare drive root, skip

            string? next = Path.GetDirectoryName(parent);
            if (next == parent)
                break;
            parent = next;
        }

        ancestors.Reverse();
        return ancestors;
    }
}
