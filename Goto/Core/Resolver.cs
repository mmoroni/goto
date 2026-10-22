using Goto.Cli;
using Goto.Search;
using Goto.Store;

namespace Goto.Core;

internal enum ResolveKind
{
    Success,
    NoMatch,
    NeedsDecision,
    TooMany,
    Listed,
    BackendError,
}

internal sealed record ResolveResult(
    ResolveKind Kind,
    string? Path,
    IReadOnlyList<string>? Candidates = null,
    string? Key = null,
    string? LeafTerm = null);

internal static class Resolver
{
    private static readonly MatchMode[] Tiers = [MatchMode.Exact, MatchMode.Prefix, MatchMode.Contains];

    public static ResolveResult Resolve(ParsedArgs args, IFolderSearcher? searcherOverride = null, Config? configOverride = null)
    {
        // Step 0 (spec section 7): single-term existing-path shortcut. Sees the
        // RAW, unsplit token -- "C:\Windows" must not be split before this check.
        // Fires even under --list/--pick: a literal existing path has nothing to list or pick.
        if (args.Positionals.Count == 1 &&
            PathShortcut.TryResolve(args.Positionals[0], out string shortcutPath))
        {
            return new ResolveResult(ResolveKind.Success, shortcutPath);
        }

        if (args.Positionals.Count == 0)
            return new ResolveResult(ResolveKind.NoMatch, null);

        GotoQuery query = GotoQuery.FromPositionals(args.Positionals);
        Config config = configOverride ?? Config.Load();

        // Step 2: DECISION lookup + stale cleanup. Skipped entirely for --list/--pick, which both
        // bypass the cache by design. (Step 1 ALIAS is deferred to Milestone 6.)
        if (!args.List && !args.Pick && DecisionStore.TryGet(query.Key, out string decidedPath))
        {
            if (Directory.Exists(decidedPath))
            {
                VisitStore.Increment(decidedPath);
                return new ResolveResult(ResolveKind.Success, decidedPath);
            }

            DecisionStore.Remove(query.Key);
        }

        // Step 3: TIERS T1-T3, stop at first tier with >=1 verified match. Applies to
        // --list/--pick too: --list shows what a plain `goto <term>` would currently consider.
        IFolderSearcher searcher;
        if (searcherOverride is not null)
        {
            searcher = searcherOverride;
        }
        else
        {
            BackendSelection selection = BackendSelector.Select(config);
            if (selection.Searcher is null)
                return new ResolveResult(ResolveKind.BackendError, selection.ErrorMessage);
            searcher = selection.Searcher;
        }

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

        // --list: full candidate set, stable sort, no navigation, no recording.
        if (args.List)
        {
            string[] all = verified.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            return new ResolveResult(ResolveKind.Listed, null, all);
        }

        // --pick: skip the decision cache (already done above) and let the caller override it.
        // A single unambiguous candidate auto-resolves rather than forcing a picker over one row
        // (see milestone-03 doc for why: it keeps `--pick` usable under --no-ui/redirected input).
        if (args.Pick)
        {
            if (verified.Length == 1)
            {
                string onlyMatch = verified[0];
                DecisionStore.Upsert(query.Key, onlyMatch);
                VisitStore.Increment(onlyMatch);
                return new ResolveResult(ResolveKind.Success, onlyMatch);
            }

            string[] all = verified.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            return new ResolveResult(ResolveKind.NeedsDecision, null, all, query.Key);
        }

        // Step 5 (M3): 1 match -> go. No dominant-winner branch -- that needs real ranking
        // scores (Milestone 5); this milestone goes straight from "1 match" to the count check.
        if (verified.Length == 1)
        {
            string chosen = verified[0];
            DecisionStore.Upsert(query.Key, chosen);
            VisitStore.Increment(chosen);
            return new ResolveResult(ResolveKind.Success, chosen);
        }

        if (verified.Length <= config.MaxCandidates)
        {
            string[] all = verified.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            return new ResolveResult(ResolveKind.NeedsDecision, null, all, query.Key);
        }

        string[] capped = verified.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        return new ResolveResult(ResolveKind.TooMany, null, capped, LeafTerm: query.Leaf);
    }

    /// <summary>
    /// Records a choice made outside Resolve's own call frame (an interactive picker selection).
    /// Reuses exactly the two calls step 6 already makes for an auto-resolved match.
    /// </summary>
    public static void RecordChoice(string key, string path)
    {
        DecisionStore.Upsert(key, path);
        VisitStore.Increment(path);
    }
}
