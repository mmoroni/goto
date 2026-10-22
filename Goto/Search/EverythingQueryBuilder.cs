namespace Goto.Search;

internal static class EverythingQueryBuilder
{
    public static string Build(string leaf, IReadOnlyList<string> qualifiers, MatchMode mode)
    {
        string term = Quote(leaf);

        string core = mode switch
        {
            MatchMode.Exact => $"folder: wfn:{term}",
            MatchMode.Prefix => $"folder: startwith:{term}",
            MatchMode.Contains => $"folder: {term}",
            MatchMode.Abbreviation => $"folder: wfn:{Quote(BuildAbbreviationPattern(leaf))}",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unsupported mode for Everything backend"),
        };

        if (qualifiers.Count == 0)
            return core;

        var parts = new List<string>(qualifiers.Count + 1) { core };
        foreach (string q in qualifiers)
            parts.Add($"path:{Quote(q)}");

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Wraps a user term in double quotes for Everything's query syntax, stripping any
    /// embedded " first (prevents operator injection via spaces/|/&lt;/&gt;/!). Wildcards
    /// * and ? are preserved as-is -- Everything treats them as wildcards inside quotes too.
    /// </summary>
    internal static string Quote(string term) => $"\"{term.Replace("\"", "")}\"";

    /// <summary>Builds the "d*o*c*" first-char-anchored wildcard pattern for Everything's wfn: operator.</summary>
    private static string BuildAbbreviationPattern(string leaf) => string.Concat(leaf.Select(c => $"{c}*"));
}
