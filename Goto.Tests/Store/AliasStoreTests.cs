using Goto.Store;
using Goto.Tests.Fakes;

namespace Goto.Tests.Store;

public class AliasStoreTests
{
    [Fact]
    public void RoundTrips_UpsertThenTryGet()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("acme");

        AliasStore.Upsert("work", target);

        Assert.True(AliasStore.TryGet("work", out string path));
        Assert.Equal(target, path, ignoreCase: true);
    }

    [Fact]
    public void Upsert_SameNameAgain_OverwritesRatherThanDuplicates()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string first = tree.CreateDir("first");
        string second = tree.CreateDir("second");

        AliasStore.Upsert("work", first);
        AliasStore.Upsert("work", second);

        Assert.True(AliasStore.TryGet("work", out string path));
        Assert.Equal(second, path, ignoreCase: true);
        Assert.Single(AliasStore.LoadAll());
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("acme");

        AliasStore.Upsert("work", target);

        Assert.True(AliasStore.TryGet("WORK", out string path));
        Assert.Equal(target, path, ignoreCase: true);
    }

    [Fact]
    public void Remove_ExistingName_RemovesIt()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        AliasStore.Upsert("work", tree.CreateDir("acme"));

        AliasStore.Remove("work");

        Assert.False(AliasStore.TryGet("work", out _));
    }

    [Fact]
    public void Remove_NonexistentName_IsNoOp()
    {
        using var home = new TempGotoHome();
        AliasStore.Remove("never-existed"); // must not throw
        Assert.Empty(AliasStore.LoadAll());
    }

    [Fact]
    public void ReadRows_CorruptLine_IsSkipped()
    {
        using var home = new TempGotoHome();
        File.WriteAllText(DataPaths.AliasesFile, "onlyonefield\n");

        Assert.Empty(AliasStore.LoadAll());
    }

    [Fact]
    public void LoadAll_ReturnsAllRows()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        AliasStore.Upsert("work", tree.CreateDir("a"));
        AliasStore.Upsert("home", tree.CreateDir("b"));

        Assert.Equal(2, AliasStore.LoadAll().Count);
    }
}
