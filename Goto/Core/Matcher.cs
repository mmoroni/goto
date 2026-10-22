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
        _ => false, // Abbreviation (T4) is Milestone 6
    };

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
