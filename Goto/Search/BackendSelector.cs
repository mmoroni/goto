using Goto.Store;

namespace Goto.Search;

internal readonly record struct BackendSelection(IFolderSearcher? Searcher, string? ErrorMessage);

internal static class BackendSelector
{
    public static BackendSelection Select(Config config)
    {
        if (config.Backend == BackendMode.Walk)
            return new BackendSelection(new WalkSearcher(config), null);

        var everything = new EverythingSearcher(config);
        bool connected = everything.TryConnect(out string? whyNot);

        if (config.Backend == BackendMode.Everything)
        {
            return connected
                ? new BackendSelection(everything, null)
                : new BackendSelection(null, $"backend 'everything' configured but unreachable: {whyNot}");
        }

        // Auto: fall back silently (rate-limited hint) rather than failing.
        if (connected)
            return new BackendSelection(everything, null);

        EverythingFallbackHint.MaybePrint(whyNot);
        return new BackendSelection(new WalkSearcher(config), null);
    }
}
