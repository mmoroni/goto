using Goto.Store;
using Goto.Tests.Fakes;

namespace Goto.Tests.Store;

public class LastResolutionStoreTests
{
    [Fact]
    public void TryGet_MissingFile_ReturnsFalse()
    {
        using var home = new TempGotoHome();
        Assert.False(LastResolutionStore.TryGet(out _, out _));
    }

    [Fact]
    public void RoundTrips_SetThenTryGet()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("documents");

        LastResolutionStore.Set("documents", target);

        Assert.True(LastResolutionStore.TryGet(out string key, out string path));
        Assert.Equal("documents", key);
        Assert.Equal(target, path, ignoreCase: true);
    }

    [Fact]
    public void Set_Twice_OverwritesRatherThanAppends()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string first = tree.CreateDir("first");
        string second = tree.CreateDir("second");

        LastResolutionStore.Set("first-key", first);
        LastResolutionStore.Set("second-key", second);

        Assert.True(LastResolutionStore.TryGet(out string key, out string path));
        Assert.Equal("second-key", key);
        Assert.Equal(second, path, ignoreCase: true);
    }

    [Fact]
    public void TryGet_CorruptLine_ReturnsFalse()
    {
        using var home = new TempGotoHome();
        File.WriteAllText(DataPaths.LastFile, "only\ttwo-fields\n");

        Assert.False(LastResolutionStore.TryGet(out _, out _));
    }
}
