namespace Goto.Store;

/// <summary>
/// Tracks the single most recent successful resolution (query key + resolved path), for
/// `--wrong`. A dedicated store rather than folded into DecisionStore: `last.txt` is a single
/// row, always fully overwritten -- a fundamentally different lifecycle than DecisionStore's
/// ever-growing keyed table.
/// </summary>
internal static class LastResolutionStore
{
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss'Z'";

    public static void Set(string key, string path)
    {
        string canonicalPath = PathCanon.Canonicalize(path);
        string timestamp = DateTime.UtcNow.ToString(TimestampFormat);

        AtomicFile.WithLock(() =>
        {
            AtomicFile.WriteAllLinesAtomic(DataPaths.LastFile,
            [
                "# key\tpath\tlastUsedUtc",
                $"{key}\t{canonicalPath}\t{timestamp}",
            ]);
        });
    }

    public static bool TryGet(out string key, out string path)
    {
        key = string.Empty;
        path = string.Empty;

        string file = DataPaths.LastFile;
        if (!File.Exists(file))
            return false;

        foreach (string line in File.ReadLines(file))
        {
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            string[] fields = line.Split('\t');
            if (fields.Length != 3)
                continue; // corrupt -- treat as absent, never throw

            key = fields[0];
            path = fields[1];
            return true;
        }

        return false;
    }
}
