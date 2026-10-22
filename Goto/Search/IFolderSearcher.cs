namespace Goto.Search;

internal enum MatchMode { Exact, Prefix, Contains, Abbreviation }

internal interface IFolderSearcher
{
    string Name { get; }
    bool TryConnect(out string? whyNot);
    IReadOnlyList<string> FindFolders(
        string leaf, IReadOnlyList<string> qualifiers,
        MatchMode mode, int limit, CancellationToken ct);
}
