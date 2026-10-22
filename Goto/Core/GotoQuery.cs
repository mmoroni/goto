namespace Goto.Core;

internal sealed record GotoQuery(IReadOnlyList<string> Qualifiers, string Leaf, string Key)
{
    public static GotoQuery FromPositionals(IReadOnlyList<string> positionals)
    {
        string[] terms = positionals
            .SelectMany(p => p.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        string key = string.Join(' ', terms.Select(t => t.ToLowerInvariant()));
        return new GotoQuery(terms[..^1], terms[^1], key);
    }
}
