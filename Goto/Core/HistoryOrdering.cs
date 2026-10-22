namespace Goto.Core;

internal static class HistoryOrdering
{
    /// <summary>
    /// Most-recently-visited first, tie-broken by visit count. Deliberately NOT Ranker.Rank:
    /// "everywhere I've been" has no single query/cwd to score against, so a full
    /// frecency+locality+project-root recompute would be a non-sequitur here. Recency is the
    /// simplest, least-surprising "history" order -- the same idiom as shell/browser history.
    /// </summary>
    public static IReadOnlyList<string> OrderByRecency(
        IReadOnlyDictionary<string, (int VisitCount, DateTime LastVisitUtc)> visits) =>
        visits
            .OrderByDescending(kv => kv.Value.LastVisitUtc)
            .ThenByDescending(kv => kv.Value.VisitCount)
            .Select(kv => kv.Key)
            .ToArray();
}
