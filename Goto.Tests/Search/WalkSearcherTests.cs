using Goto.Search;
using Goto.Store;
using Goto.Tests.Fakes;

namespace Goto.Tests.Search;

public class WalkSearcherTests
{
    // Tests build temp trees via Directory.CreateTempSubdirectory(), which places them under
    // %LOCALAPPDATA%\Temp. The real default exclude list has an "AppData\Local\Temp" entry
    // (to keep the walker out of the user's own temp/cache churn), which would otherwise prune
    // every test fixture. Tests explicitly override Exclude to opt back in.

    [Fact]
    public void FindFolders_ExactMatch_FindsTopLevelFolder()
    {
        using var tree = new TempTree();
        string expected = tree.CreateDir("documents");
        var config = new Config { WalkRoots = [tree.Root], WalkTimeoutMs = 5000, Exclude = [] };
        var searcher = new WalkSearcher(config);

        var results = searcher.FindFolders("documents", [], MatchMode.Exact, limit: 100, CancellationToken.None);

        Assert.Contains(expected, results, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindFolders_ContainsMode_FindsNestedFolderByContains()
    {
        using var tree = new TempTree();
        string expected = tree.CreateDir(@"a\b\my-documents");
        var config = new Config { WalkRoots = [tree.Root], WalkTimeoutMs = 5000, Exclude = [] };
        var searcher = new WalkSearcher(config);

        var exact = searcher.FindFolders("documents", [], MatchMode.Exact, limit: 100, CancellationToken.None);
        Assert.Empty(exact);

        var contains = searcher.FindFolders("documents", [], MatchMode.Contains, limit: 100, CancellationToken.None);
        Assert.Contains(expected, contains, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindFolders_ExcludedSubtree_IsPruned()
    {
        using var tree = new TempTree();
        tree.CreateDir(@"node_modules\documents");
        var config = new Config { WalkRoots = [tree.Root], WalkTimeoutMs = 5000, Exclude = ["node_modules"] };
        var searcher = new WalkSearcher(config);

        var results = searcher.FindFolders("documents", [], MatchMode.Exact, limit: 100, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public void FindFolders_StopsAtFirstRootWithMatches()
    {
        using var treeA = new TempTree(); // no match -- forces fall-through to treeB
        using var treeB = new TempTree();
        string expectedInB = treeB.CreateDir("documents");
        var config = new Config { WalkRoots = [treeA.Root, treeB.Root], WalkTimeoutMs = 5000, Exclude = [] };
        var searcher = new WalkSearcher(config);

        var results = searcher.FindFolders("documents", [], MatchMode.Exact, limit: 100, CancellationToken.None);

        Assert.Contains(expectedInB, results, StringComparer.OrdinalIgnoreCase);
    }
}
