using Goto.Search;

namespace Goto.Tests.Fakes;

internal sealed class FakeFolderSearcher(IReadOnlyList<string> results, Action? onCalled = null) : IFolderSearcher
{
    public string Name => "fake";

    public bool TryConnect(out string? whyNot)
    {
        whyNot = null;
        return true;
    }

    public IReadOnlyList<string> FindFolders(
        string leaf, IReadOnlyList<string> qualifiers,
        MatchMode mode, int limit, CancellationToken ct)
    {
        onCalled?.Invoke();
        return results;
    }
}
