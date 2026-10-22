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

internal enum ExplainSource { PathShortcut, Alias, Decision, Tier }

internal sealed record ExplainTrace(ExplainSource Source, MatchMode? Tier, IReadOnlyList<ScoreBreakdown> Breakdown);

internal sealed record ResolveResult(
    ResolveKind Kind,
    string? Path,
    IReadOnlyList<string>? Candidates = null,
    string? Key = null,
    string? LeafTerm = null,
    ExplainTrace? Explain = null,
    IReadOnlyList<string>? DidYouMean = null);

internal static class Resolver
{
    private static readonly MatchMode[] Tiers =
        [MatchMode.Exact, MatchMode.Prefix, MatchMode.Contains, MatchMode.Abbreviation];
    private const int ExistenceCheckTopN = 50;

    public static ResolveResult Resolve(
        ParsedArgs args, IFolderSearcher? searcherOverride = null,
        Config? configOverride = null, string? excludePath = null)
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

        // Step 1 (spec section 7): ALIAS lookup. "@name" or a bare exact alias-name match. Same
        // single-term gate as step 0 -- a multi-term query is a qualifier+leaf search, never an
        // alias lookup, even if the first term happens to collide with an alias name. Fires even
        // under --list/--pick (same reasoning as step 0: a deliberate bookmark has no candidate
        // set to list or picker to force). No existence check or stale-entry cleanup here, unlike
        // step 2's decision cache -- an alias is a manual, user-owned bookmark, not goto's
        // opportunistic cache; auto-deleting it because the target is momentarily missing (e.g.
        // an unmounted share) would be surprising. Let Set-Location surface the error downstream.
        if (args.Positionals.Count == 1)
        {
            string raw = args.Positionals[0];
            string candidateName = raw.StartsWith('@') ? raw[1..] : raw;

            if (candidateName.Length > 0
                && !candidateName.Contains('/') && !candidateName.Contains('\\')
                && AliasStore.TryGet(candidateName, out string aliasPath))
            {
                return new ResolveResult(ResolveKind.Success, aliasPath,
                    Explain: new ExplainTrace(ExplainSource.Alias, null, []));
            }
        }

        if (args.Positionals.Count == 0)
            return new ResolveResult(ResolveKind.NoMatch, null);

        GotoQuery query = GotoQuery.FromPositionals(args.Positionals);
        Config config = configOverride ?? Config.Load();

        // Step 2: DECISION lookup + stale cleanup. Skipped entirely for --list/--pick, which both
        // bypass the cache by design.
        if (!args.List && !args.Pick && DecisionStore.TryGet(query.Key, out string decidedPath))
        {
            if (Directory.Exists(decidedPath))
            {
                VisitStore.Increment(decidedPath);
                LastResolutionStore.Set(query.Key, decidedPath);
                return new ResolveResult(ResolveKind.Success, decidedPath,
                    Explain: new ExplainTrace(ExplainSource.Decision, null, []));
            }

            DecisionStore.Remove(query.Key);
        }

        // Step 3: TIERS T1-T4, stop at first tier with >=1 verified match. Applies to
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

        using var cts = new CancellationTokenSource(config.SearchTimeoutMs);

        string[] verified = [];
        MatchMode matchedTier = default;
        foreach (MatchMode mode in Tiers)
        {
            IReadOnlyList<string> raw = searcher.FindFolders(query.Leaf, query.Qualifiers, mode, limit: 2000, cts.Token);
            string[] candidates = raw
                .Where(p => Matcher.IsMatch(p, query, mode))
                .Where(p => !config.IsExcluded(p))
                .Where(p => excludePath is null ||
                            !string.Equals(PathCanon.Canonicalize(p), excludePath, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Length > 0)
            {
                verified = candidates;
                matchedTier = mode;
                break;
            }
        }

        // Step 4: no tier matched at all. Typo tolerance: compare the leaf term against leaf
        // names already present in visits.tsv (no filesystem search needed).
        if (verified.Length == 0)
        {
            IReadOnlyList<string> suggestions = ComputeDidYouMean(query.Leaf);
            return new ResolveResult(ResolveKind.NoMatch, null,
                DidYouMean: suggestions.Count > 0 ? suggestions : null);
        }

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
                RecordChoice(query.Key, ordered[0]);
                return new ResolveResult(ResolveKind.Success, ordered[0], Explain: explain);
            }

            return new ResolveResult(ResolveKind.NeedsDecision, null, ordered, query.Key, Explain: explain);
        }

        // Step 5 (spec section 7): 1 match -> go.
        if (ordered.Length == 1)
        {
            RecordChoice(query.Key, ordered[0]);
            return new ResolveResult(ResolveKind.Success, ordered[0], Explain: explain);
        }

        // Dominant winner: top score comfortably clears the second by at least the margin.
        double margin = existing[0].Total - existing[1].Total;
        if (margin >= config.DominanceMargin)
        {
            RecordChoice(query.Key, ordered[0]);
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
    /// Compares the leaf term against leaf names already present in visits.tsv using edit
    /// distance ≤ 2, for a "did you mean X?" suggestion when nothing matched at all.
    /// </summary>
    private static IReadOnlyList<string> ComputeDidYouMean(string leafTerm)
    {
        IReadOnlyDictionary<string, (int VisitCount, DateTime LastVisitUtc)> visits = VisitStore.LoadAll();

        return visits.Keys
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => (Name: n!, Distance: EditDistance.Levenshtein(n!, leafTerm)))
            .Where(x => x.Distance <= 2)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(x => x.Name)
            .ToArray();
    }

    /// <summary>
    /// Records a choice made outside Resolve's own call frame (an interactive picker selection),
    /// and the single write path every auto-resolve branch inside Resolve itself uses too.
    /// </summary>
    public static void RecordChoice(string key, string path)
    {
        DecisionStore.Upsert(key, path);
        VisitStore.Increment(path);
        LastResolutionStore.Set(key, path);
    }
}
