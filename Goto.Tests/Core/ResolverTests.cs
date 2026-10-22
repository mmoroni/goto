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
    public void Resolve_MultipleVerifiedMatches_ReturnsTooManyWithCappedCandidates()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string first = tree.CreateDir("documents");
        string second = tree.CreateDir(@"other\documents");

        var fake = new FakeFolderSearcher([first, second]);

        var result = Resolver.Resolve(ArgParser.Parse(["documents"]), fake, new Config { Exclude = [] });

        Assert.Equal(ResolveKind.TooMany, result.Kind);
        Assert.NotNull(result.Candidates);
        Assert.Equal(2, result.Candidates!.Count);
    }
}
