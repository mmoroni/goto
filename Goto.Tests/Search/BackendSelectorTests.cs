using Goto.Search;
using Goto.Store;
using Goto.Tests.Fakes;

namespace Goto.Tests.Search;

public class BackendSelectorTests
{
    [Fact]
    public void Select_BackendWalk_ReturnsWalkSearcherRegardlessOfEverythingAvailability()
    {
        using var home = new TempGotoHome();
        var config = new Config { Backend = BackendMode.Walk };

        BackendSelection selection = BackendSelector.Select(config);

        Assert.NotNull(selection.Searcher);
        Assert.Equal("walk", selection.Searcher!.Name);
        Assert.Null(selection.ErrorMessage);
    }
}
