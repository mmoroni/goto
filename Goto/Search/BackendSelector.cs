using Goto.Store;

namespace Goto.Search;

internal readonly record struct BackendSelection(IFolderSearcher? Searcher, string? ErrorMessage);

internal static class BackendSelector
{
    // Everything is the only backend: the filesystem-walk fallback was removed (it could take
    // many seconds on a large drive and didn't respond to Ctrl+C while scanning). If Everything
    // isn't reachable, resolution fails fast with a clear error instead of silently degrading to
    // a slow, uninterruptible scan.
    public static BackendSelection Select(Config config)
    {
        var everything = new EverythingSearcher(config);
        bool connected = everything.TryConnect(out string? whyNot);

        return connected
            ? new BackendSelection(everything, null)
            : new BackendSelection(null, $"Everything unreachable: {whyNot}");
    }
}
