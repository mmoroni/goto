using Goto.Cli;
using Goto.Core;
using Goto.Search;
using Goto.Store;
using Goto.Tests.Fakes;

namespace Goto.Tests.Core;

public class ResolverTests
{
    [Fact]
    public void Resolve_WithExistingDecision_ReturnsPathWithoutCallingSearcher()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("documents");

        DecisionStore.Upsert("documents", target);

        bool searcherCalled = false;
        var fake = new FakeFolderSearcher([], onCalled: () => searcherCalled = true);

        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake);

        Assert.Equal(ResolveKind.Success, result.Kind);
        Assert.Equal(target, result.Path, ignoreCase: true);
        Assert.False(searcherCalled, "a remembered decision must short-circuit the search entirely");
    }

    [Fact]
    public void Resolve_WithStaleDecision_RemovesItAndResearches()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();

        string staleTarget = Path.Combine(tree.Root, "deleted-documents");
        DecisionStore.Upsert("documents", staleTarget); // points at a path that doesn't exist

        string freshTarget = tree.CreateDir("documents");
        bool searcherCalled = false;
        var fake = new FakeFolderSearcher([freshTarget], onCalled: () => searcherCalled = true);

        // Exclude = [] because temp test trees live under AppData\Local\Temp, which the real
        // default exclude list prunes.
        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake, new Config { Exclude = [] });

        Assert.Equal(ResolveKind.Success, result.Kind);
        Assert.Equal(freshTarget, result.Path, ignoreCase: true);
        Assert.True(searcherCalled, "a stale decision must be discarded and a fresh search performed");

        Assert.True(DecisionStore.TryGet("documents", out string updatedPath));
        Assert.Equal(freshTarget, updatedPath, ignoreCase: true);
    }

    [Fact]
    public void Resolve_NoMatches_ReturnsNoMatch()
    {
        using var home = new TempGotoHome();
        var fake = new FakeFolderSearcher([]);

        var result = Resolver.Resolve(ArgParser.Parse(["nonexistent-xyz"]), fake);

        Assert.Equal(ResolveKind.NoMatch, result.Kind);
    }

    [Fact]
    public void Resolve_CountAboveMaxCandidates_ReturnsTooManyCappedAt20()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string first = tree.CreateDir("documents");
        string second = tree.CreateDir(@"other\documents");

        var fake = new FakeFolderSearcher([first, second]);

        // MaxCandidates = 1 forces the 2-match case over the TooMany threshold.
        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake, new Config { Exclude = [], MaxCandidates = 1 });

        Assert.Equal(ResolveKind.TooMany, result.Kind);
        Assert.NotNull(result.Candidates);
        Assert.Equal(2, result.Candidates!.Count);
    }

    [Fact]
    public void Resolve_CountWithinMaxCandidates_ReturnsNeedsDecisionWithKeyAndAllCandidates()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string first = tree.CreateDir("documents");
        string second = tree.CreateDir(@"other\documents");

        var fake = new FakeFolderSearcher([first, second]);

        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake, new Config { Exclude = [], MaxCandidates = 15 });

        Assert.Equal(ResolveKind.NeedsDecision, result.Kind);
        Assert.Equal("documents", result.Key);
        Assert.Equal(2, result.Candidates!.Count);
    }

    [Fact]
    public void Resolve_List_SkipsDecisionCache_ReturnsFullSet_RecordsNothing()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string decided = tree.CreateDir("decided-elsewhere");
        DecisionStore.Upsert("documents", decided);

        string fresh = tree.CreateDir("documents");
        var fake = new FakeFolderSearcher([fresh]);

        var parsed = ArgParser.Parse(["--list", "documents"]);
        var result = Resolver.Resolve(parsed, fake, new Config { Exclude = [] });

        Assert.Equal(ResolveKind.Listed, result.Kind);
        Assert.Single(result.Candidates!);
        Assert.Equal(fresh, result.Candidates![0], ignoreCase: true);

        // The pre-existing decision must be untouched -- --list never writes.
        Assert.True(DecisionStore.TryGet("documents", out string stillDecided));
        Assert.Equal(decided, stillDecided, ignoreCase: true);
    }

    [Fact]
    public void Resolve_Pick_SingleMatch_AutoResolvesWithoutPicker()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string other = tree.CreateDir("decided-elsewhere");
        DecisionStore.Upsert("documents", other); // stale/irrelevant decision -- --pick ignores it

        string fresh = tree.CreateDir("documents");
        var fake = new FakeFolderSearcher([fresh]);

        var parsed = ArgParser.Parse(["--pick", "documents"]);
        var result = Resolver.Resolve(parsed, fake, new Config { Exclude = [] });

        Assert.Equal(ResolveKind.Success, result.Kind);
        Assert.Equal(fresh, result.Path, ignoreCase: true);

        Assert.True(DecisionStore.TryGet("documents", out string updated));
        Assert.Equal(fresh, updated, ignoreCase: true);
    }

    [Fact]
    public void Resolve_Pick_MultipleMatches_ReturnsNeedsDecisionBypassingMaxCandidates()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        var candidates = Enumerable.Range(0, 5)
            .Select(i => tree.CreateDir($"folder{i}\\documents"))
            .ToArray();

        var fake = new FakeFolderSearcher(candidates);

        var parsed = ArgParser.Parse(["--pick", "documents"]);
        var result = Resolver.Resolve(parsed, fake, new Config { Exclude = [], MaxCandidates = 1 });

        Assert.Equal(ResolveKind.NeedsDecision, result.Kind);
        Assert.Equal(5, result.Candidates!.Count);
        Assert.Equal("documents", result.Key);
    }

    [Fact]
    public void Resolve_DominantWinner_AutoResolvesWithoutNeedsDecision()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        // Same leaf ("documents") under differently-named parents -- "zz-parent" is
        // alphabetically last, proving ranking (not alphabetical order) drives the outcome.
        string winner = tree.CreateDir(@"zz-parent\documents");
        string loser = tree.CreateDir(@"aa-parent\documents");

        for (int i = 0; i < 21; i++)
            VisitStore.Increment(winner); // large visit count -> large frecency gap over the unvisited loser

        var fake = new FakeFolderSearcher([winner, loser]);
        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake, new Config { Exclude = [] });

        Assert.Equal(ResolveKind.Success, result.Kind);
        Assert.Equal(winner, result.Path, ignoreCase: true);

        Assert.True(DecisionStore.TryGet("documents", out string decided));
        Assert.Equal(winner, decided, ignoreCase: true);
    }

    [Fact]
    public void Resolve_MarginNotMet_StillReturnsNeedsDecision()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string first = tree.CreateDir(@"a-parent\documents");
        string second = tree.CreateDir(@"b-parent\documents");

        var fake = new FakeFolderSearcher([first, second]);
        // No visits seeded for either -- any score gap is small (depth-only), well under the margin.
        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake, new Config { Exclude = [] });

        Assert.Equal(ResolveKind.NeedsDecision, result.Kind);
    }

    [Fact]
    public void Resolve_NeedsDecision_CandidatesInRankedOrder_NotAlphabetical()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string alphabeticallyFirst = tree.CreateDir(@"aa-parent\documents");
        string higherScoring = tree.CreateDir(@"zz-parent\documents");

        // Visit the alphabetically-later folder enough to outrank the earlier one, but keep
        // the gap below the dominance margin so this still returns NeedsDecision (not Success).
        VisitStore.Increment(higherScoring);

        var fake = new FakeFolderSearcher([alphabeticallyFirst, higherScoring]);
        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake,
            new Config { Exclude = [], DominanceMargin = 1000 }); // unreachable margin -> forces NeedsDecision

        Assert.Equal(ResolveKind.NeedsDecision, result.Kind);
        Assert.Equal(higherScoring, result.Candidates![0], ignoreCase: true);
    }

    [Fact]
    public void Resolve_PhantomTopRankedCandidate_DroppedBeforeCountBranch()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        // Same leaf ("documents") under different parents, so Matcher's Exact tier accepts
        // BOTH as candidates -- phantom must only be removed by the existence check, not
        // simply never matched in the first place.
        string real = tree.CreateDir(@"real-parent\documents");
        string phantom = Path.Combine(tree.Root, "phantom-parent", "documents"); // never created on disk

        // Heavily visit the phantom so it would rank #1 if existence weren't checked.
        for (int i = 0; i < 20; i++)
            VisitStore.Increment(phantom);

        var fake = new FakeFolderSearcher([phantom, real]);
        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake, new Config { Exclude = [] });

        Assert.Equal(ResolveKind.Success, result.Kind);
        Assert.Equal(real, result.Path, ignoreCase: true);
    }

    [Fact]
    public void Resolve_Pick_NeverAutoResolvesOnDominantScoreGap()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string heavilyVisited = tree.CreateDir(@"zz-parent\documents");
        string unvisited = tree.CreateDir(@"aa-parent\documents");

        for (int i = 0; i < 20; i++)
            VisitStore.Increment(heavilyVisited);

        var parsed = ArgParser.Parse(["--pick", "documents"]);
        var fake = new FakeFolderSearcher([heavilyVisited, unvisited]);
        var result = Resolver.Resolve(parsed, fake, new Config { Exclude = [] });

        // Even though heavilyVisited would dominate under the normal path, --pick always
        // forces a decision among 2+ candidates -- dominant-winner is a separate branch
        // --pick deliberately bypasses.
        Assert.Equal(ResolveKind.NeedsDecision, result.Kind);
        Assert.Equal(2, result.Candidates!.Count);
    }

    [Fact]
    public void Resolve_List_ReturnsRankedOrder()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string alphabeticallyFirst = tree.CreateDir(@"aa-parent\documents");
        string higherScoring = tree.CreateDir(@"zz-parent\documents");

        VisitStore.Increment(higherScoring);

        var parsed = ArgParser.Parse(["--list", "documents"]);
        var fake = new FakeFolderSearcher([alphabeticallyFirst, higherScoring]);
        var result = Resolver.Resolve(parsed, fake, new Config { Exclude = [] });

        Assert.Equal(ResolveKind.Listed, result.Kind);
        Assert.Equal(higherScoring, result.Candidates![0], ignoreCase: true);
    }

    [Fact]
    public void Resolve_Explain_PopulatesBreakdownForTierSearch()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("documents");

        var fake = new FakeFolderSearcher([target]);
        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake, new Config { Exclude = [] });

        Assert.NotNull(result.Explain);
        Assert.Equal(ExplainSource.Tier, result.Explain!.Source);
        Assert.Single(result.Explain.Breakdown);
        Assert.Equal(target, result.Explain.Breakdown[0].Path, ignoreCase: true);
    }

    [Fact]
    public void Resolve_Explain_DecisionHit_SourceIsDecisionWithEmptyBreakdown()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("documents");
        DecisionStore.Upsert("documents", target);

        var fake = new FakeFolderSearcher([]);
        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake);

        Assert.NotNull(result.Explain);
        Assert.Equal(ExplainSource.Decision, result.Explain!.Source);
        Assert.Empty(result.Explain.Breakdown);
    }

    [Fact]
    public void RecordChoice_UpsertsDecisionAndIncrementsVisit()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("chosen");

        Resolver.RecordChoice("some key", target);

        Assert.True(DecisionStore.TryGet("some key", out string decided));
        Assert.Equal(target, decided, ignoreCase: true);

        string visitsContent = File.ReadAllText(DataPaths.VisitsFile);
        Assert.Contains(target, visitsContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordChoice_AlsoWritesLastResolution()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("chosen");

        Resolver.RecordChoice("some key", target);

        Assert.True(LastResolutionStore.TryGet(out string key, out string path));
        Assert.Equal("some key", key);
        Assert.Equal(target, path, ignoreCase: true);
    }

    [Fact]
    public void Resolve_SingleMatchAutoResolve_WritesLastResolution()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("documents");

        var fake = new FakeFolderSearcher([target]);
        Resolver.Resolve(ArgParser.Parse(["documents"]), fake, new Config { Exclude = [] });

        Assert.True(LastResolutionStore.TryGet(out string key, out string path));
        Assert.Equal("documents", key);
        Assert.Equal(target, path, ignoreCase: true);
    }

    [Fact]
    public void Resolve_DecisionCacheHit_AlsoWritesLastResolution()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("documents");
        DecisionStore.Upsert("documents", target);

        var fake = new FakeFolderSearcher([]);
        Resolver.Resolve(ArgParser.Parse(["documents"]), fake);

        Assert.True(LastResolutionStore.TryGet(out string key, out string path));
        Assert.Equal("documents", key);
        Assert.Equal(target, path, ignoreCase: true);
    }

    [Fact]
    public void Resolve_AliasLookup_FiresForAtPrefixedName()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("acme");
        AliasStore.Upsert("work", target);

        var result = Resolver.Resolve(ArgParser.Parse(["@work"]));

        Assert.Equal(ResolveKind.Success, result.Kind);
        Assert.Equal(target, result.Path, ignoreCase: true);
        Assert.Equal(ExplainSource.Alias, result.Explain!.Source);
    }

    [Fact]
    public void Resolve_AliasLookup_FiresForBareName()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("acme");
        AliasStore.Upsert("work", target);

        var result = Resolver.Resolve(ArgParser.Parse(["work"]));

        Assert.Equal(ResolveKind.Success, result.Kind);
        Assert.Equal(target, result.Path, ignoreCase: true);
    }

    [Fact]
    public void Resolve_AliasLookup_SkippedForMultiTermQuery()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        AliasStore.Upsert("work", tree.CreateDir("acme"));

        // "work" is an alias, but a second term makes this a qualifier+leaf search, not a
        // bookmark lookup -- must fall through to the normal tiered search (which finds nothing).
        var fake = new FakeFolderSearcher([]);
        var result = Resolver.Resolve(ArgParser.Parse(["work", "documents"]), fake);

        Assert.Equal(ResolveKind.NoMatch, result.Kind);
    }

    [Fact]
    public void Resolve_AliasLookup_SkippedForTokenContainingSlash()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        AliasStore.Upsert("work", tree.CreateDir("acme"));

        var fake = new FakeFolderSearcher([]);
        var result = Resolver.Resolve(ArgParser.Parse(["work/documents"]), fake);

        Assert.Equal(ResolveKind.NoMatch, result.Kind);
    }

    [Fact]
    public void Resolve_AliasLookup_FiresUnderListAndPick()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("acme");
        AliasStore.Upsert("work", target);

        var listResult = Resolver.Resolve(ArgParser.Parse(["--list", "@work"]));
        var pickResult = Resolver.Resolve(ArgParser.Parse(["--pick", "@work"]));

        Assert.Equal(ResolveKind.Success, listResult.Kind);
        Assert.Equal(target, listResult.Path, ignoreCase: true);
        Assert.Equal(ResolveKind.Success, pickResult.Kind);
        Assert.Equal(target, pickResult.Path, ignoreCase: true);
    }

    [Fact]
    public void Resolve_AbbreviationTier_ReachedWhenNoOtherTierMatches()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("Downloads");

        // "dl" doesn't exact/prefix/contains-match "Downloads" -- only T4 Abbreviation does.
        var fake = new FakeFolderSearcher([target]);
        var result = Resolver.Resolve(ArgParser.Parse(["dl"]), fake, new Config { Exclude = [] });

        Assert.Equal(ResolveKind.Success, result.Kind);
        Assert.Equal(target, result.Path, ignoreCase: true);
        Assert.Equal(MatchMode.Abbreviation, result.Explain!.Tier);
    }

    [Fact]
    public void Resolve_NoTierMatchedAtAll_PopulatesDidYouMean()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string visited = tree.CreateDir("Documents");
        VisitStore.Increment(visited); // seeds "Documents" as a known leaf name

        var fake = new FakeFolderSearcher([]); // no tier finds anything at all
        var result = Resolver.Resolve(ArgParser.Parse(["Documnts"]), fake); // one edit away

        Assert.Equal(ResolveKind.NoMatch, result.Kind);
        Assert.NotNull(result.DidYouMean);
        Assert.Contains("Documents", result.DidYouMean!);
    }

    [Fact]
    public void Resolve_PhantomOnlyNoMatch_DoesNotPopulateDidYouMean()
    {
        using var home = new TempGotoHome();
        // Separate trees so the real "Documents" dir and the phantom "documents" path can
        // never accidentally be the SAME directory on NTFS's case-insensitive filesystem.
        using var visitedTree = new TempTree();
        using var phantomTree = new TempTree();
        string visited = visitedTree.CreateDir("Documents");
        VisitStore.Increment(visited);

        // A tier DID match something (a phantom), so this is the "stale, not a typo" NoMatch --
        // did-you-mean must not fire here even though "Documents" is a known close leaf name.
        string phantom = Path.Combine(phantomTree.Root, "documents"); // never created on disk
        var fake = new FakeFolderSearcher([phantom]);
        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake, new Config { Exclude = [] });

        Assert.Equal(ResolveKind.NoMatch, result.Kind);
        Assert.Null(result.DidYouMean);
    }

    [Fact]
    public void Resolve_ExcludePath_DropsExcludedCandidate()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string first = tree.CreateDir(@"a-parent\documents");
        string second = tree.CreateDir(@"b-parent\documents");

        var fake = new FakeFolderSearcher([first, second]);
        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake,
            new Config { Exclude = [] }, excludePath: first);

        Assert.Equal(ResolveKind.Success, result.Kind);
        Assert.Equal(second, result.Path, ignoreCase: true);
    }

    [Fact]
    public void Resolve_ExcludePath_LastRemainingCandidateExcluded_ReturnsNoMatch()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string only = tree.CreateDir("documents");

        var fake = new FakeFolderSearcher([only]);
        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake,
            new Config { Exclude = [] }, excludePath: only);

        Assert.Equal(ResolveKind.NoMatch, result.Kind);
    }
}
