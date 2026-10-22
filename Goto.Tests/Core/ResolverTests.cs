using Goto.Cli;
using Goto.Core;
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
}
