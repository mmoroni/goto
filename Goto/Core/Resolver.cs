using Goto.Cli;
using Goto.Search;
using Goto.Store;

namespace Goto.Core;

internal enum ResolveKind
{
    Success,
    NoMatch,
    TooMany,
}

internal sealed record ResolveResult(ResolveKind Kind, string? Path, IReadOnlyList<string>? Candidates = null);

internal static class Resolver
{
    private static readonly MatchMode[] Tiers = [MatchMode.Exact, MatchMode.Prefix, MatchMode.Contains];

    public static ResolveResult Resolve(ParsedArgs args, IFolderSearcher? searcherOverride = null, Config? configOverride = null)
    {
        // Step 0 (spec section 7): single-term existing-path shortcut. Sees the
        // RAW, unsplit token -- "C:\Windows" must not be split before this check.
        if (args.Positionals.Count == 1 &&
            PathShortcut.TryResolve(args.Positionals[0], out string shortcutPath))
        {
            return new ResolveResult(ResolveKind.Success, shortcutPath);
        }

        if (args.Positionals.Count == 0)
            return new ResolveResult(ResolveKind.NoMatch, null);

        GotoQuery query = GotoQuery.FromPositionals(args.Positionals);
        Config config = configOverride ?? Config.Load();

        // Step 2: DECISION lookup + stale cleanup. (Step 1 ALIAS is deferred to Milestone 6.)
        if (DecisionStore.TryGet(query.Key, out string decidedPath))
        {
            if (Directory.Exists(decidedPath))
            {
                VisitStore.Increment(decidedPath);
                return new ResolveResult(ResolveKind.Success, decidedPath);
            }

            DecisionStore.Remove(query.Key);
        }

        // Step 3: TIERS T1-T3, stop at first tier with >=1 verified match.
        IFolderSearcher searcher = searcherOverride ?? new WalkSearcher(config);
        using var cts = new CancellationTokenSource(config.WalkTimeoutMs);

        string[] verified = [];
        foreach (MatchMode mode in Tiers)
        {
            IReadOnlyList<string> raw = searcher.FindFolders(query.Leaf, query.Qualifiers, mode, limit: 2000, cts.Token);
            verified = raw
                .Where(p => Matcher.IsMatch(p, query, mode))
                .Where(p => !config.IsExcluded(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (verified.Length > 0)
                break;
        }

        if (searcher is WalkSearcher { LastSearchWasIncomplete: true })
            Console.Error.WriteLine("gt: (search incomplete)");

        // Step 4: no tier matched.
        if (verified.Length == 0)
            return new ResolveResult(ResolveKind.NoMatch, null);

        // Step 5 (M2-simplified: no ranking/picker yet -- see milestone-02 doc for rationale).
        if (verified.Length > 1)
        {
            string[] capped = verified.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
            return new ResolveResult(ResolveKind.TooMany, null, capped);
        }

        string chosen = verified[0];

        // Step 6: upsert decision, increment visit.
        DecisionStore.Upsert(query.Key, chosen);
        VisitStore.Increment(chosen);
        return new ResolveResult(ResolveKind.Success, chosen);
    }
}
