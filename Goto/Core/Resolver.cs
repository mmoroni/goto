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

internal enum ExplainSource { PathShortcut, Decision, Tier }

internal sealed record ExplainTrace(ExplainSource Source, MatchMode? Tier, IReadOnlyList<ScoreBreakdown> Breakdown);

internal sealed record ResolveResult(
    ResolveKind Kind,
    string? Path,
    IReadOnlyList<string>? Candidates = null,
    string? Key = null,
    string? LeafTerm = null,
    ExplainTrace? Explain = null);

internal static class Resolver
{
    private static readonly MatchMode[] Tiers = [MatchMode.Exact, MatchMode.Prefix, MatchMode.Contains];
    private const int ExistenceCheckTopN = 50;

    public static ResolveResult Resolve(ParsedArgs args, IFolderSearcher? searcherOverride = null, Config? configOverride = null)
    {
        // Step 0 (spec section 7): single-term existing-path shortcut. Sees the
        // RAW, unsplit token -- "C:\Windows" must not be split before this check.
        // Fires even under --list/--pick: a literal existing path has nothing to list or pick.
        if (args.Positionals.Count == 1 &&
            PathShortcut.TryResolve(args.Positionals[0], out string shortcutPath))
        {
            return new ResolveResult(ResolveKind.Success, shortcutPath,
                Explain: new ExplainTrace(ExplainSource.PathShortcut, null, []));
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
                return new ResolveResult(ResolveKind.Success, decidedPath,
                    Explain: new ExplainTrace(ExplainSource.Decision, null, []));
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
        MatchMode matchedTier = default;
        foreach (MatchMode mode in Tiers)
        {
            IReadOnlyList<string> raw = searcher.FindFolders(query.Leaf, query.Qualifiers, mode, limit: 2000, cts.Token);
            string[] candidates = raw
                .Where(p => Matcher.IsMatch(p, query, mode))
                .Where(p => !config.IsExcluded(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Length > 0)
            {
                verified = candidates;
                matchedTier = mode;
                break;
            }
        }

        if (searcher is WalkSearcher { LastSearchWasIncomplete: true })
            Console.Error.WriteLine("gt: (search incomplete)");

        // Step 4: no tier matched.
        if (verified.Length == 0)
            return new ResolveResult(ResolveKind.NoMatch, null);

        // Rank, sort descending, drop phantom (stale) top candidates -- shared by every mode below.
        IReadOnlyDictionary<string, (int VisitCount, DateTime LastVisitUtc)> visits = VisitStore.LoadAll();
        string cwd = Environment.CurrentDirectory;
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ScoreBreakdown[] ranked = Ranker.Rank(verified, visits, cwd, userProfile, DateTime.UtcNow)
            .OrderByDescending(b => b.Total)
            .ToArray();
        ScoreBreakdown[] existing = FilterExisting(ranked);

        if (existing.Length == 0)
            return new ResolveResult(ResolveKind.NoMatch, null); // all top candidates were phantom

        var explain = new ExplainTrace(ExplainSource.Tier, matchedTier, existing);
        string[] ordered = existing.Select(b => b.Path).ToArray();

        // --list: full ranked candidate set, no navigation, no recording.
        if (args.List)
            return new ResolveResult(ResolveKind.Listed, null, ordered, Explain: explain);

        // --pick: skip the decision cache (already done above) and let the caller override it.
        // A single unambiguous candidate auto-resolves rather than forcing a picker over one row
        // (see milestone-03 doc for why). Never auto-resolves on a dominant score gap -- that's
        // specifically the no-flag "let goto decide for you" behavior below, which --pick bypasses
        // on purpose since its whole point is forcing explicit confirmation.
        if (args.Pick)
        {
            if (ordered.Length == 1)
            {
                DecisionStore.Upsert(query.Key, ordered[0]);
                VisitStore.Increment(ordered[0]);
                return new ResolveResult(ResolveKind.Success, ordered[0], Explain: explain);
            }

            return new ResolveResult(ResolveKind.NeedsDecision, null, ordered, query.Key, Explain: explain);
        }

        // Step 5 (spec section 7): 1 match -> go.
        if (ordered.Length == 1)
        {
            DecisionStore.Upsert(query.Key, ordered[0]);
            VisitStore.Increment(ordered[0]);
            return new ResolveResult(ResolveKind.Success, ordered[0], Explain: explain);
        }

        // Dominant winner: top score comfortably clears the second by at least the margin.
        double margin = existing[0].Total - existing[1].Total;
        if (margin >= config.DominanceMargin)
        {
            DecisionStore.Upsert(query.Key, ordered[0]);
            VisitStore.Increment(ordered[0]);
            return new ResolveResult(ResolveKind.Success, ordered[0], Explain: explain);
        }

        if (ordered.Length <= config.MaxCandidates)
            return new ResolveResult(ResolveKind.NeedsDecision, null, ordered, query.Key, Explain: explain);

        return new ResolveResult(ResolveKind.TooMany, null, ordered.Take(20).ToArray(), LeafTerm: query.Leaf, Explain: explain);
    }

    /// <summary>
    /// Only the top ~50 ranked candidates are existence-checked (Everything's index can
    /// briefly include deleted folders) -- never the full result set. Phantoms are dropped,
    /// not backfilled from beyond rank 50, so the array can shrink before any count-based
    /// branching (1-match / dominant-winner / NeedsDecision) is evaluated.
    /// </summary>
    private static ScoreBreakdown[] FilterExisting(ScoreBreakdown[] rankedDescending)
    {
        var result = new List<ScoreBreakdown>(rankedDescending.Length);
        for (int i = 0; i < rankedDescending.Length; i++)
        {
            if (i < ExistenceCheckTopN)
            {
                if (Directory.Exists(rankedDescending[i].Path))
                    result.Add(rankedDescending[i]);
            }
            else
            {
                result.Add(rankedDescending[i]); // beyond the checked window: assumed to exist
            }
        }

        return result.ToArray();
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
