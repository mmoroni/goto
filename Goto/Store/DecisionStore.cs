namespace Goto.Store;

internal static class DecisionStore
{
    public static bool TryGet(string key, out string path)
    {
        path = string.Empty;

        foreach (var row in ReadRows())
        {
            if (string.Equals(row.Key, key, StringComparison.Ordinal))
            {
                path = row.Path;
                return true;
            }
        }

        return false;
    }

    public static void Upsert(string key, string path)
    {
        string canonicalPath = Canonicalize(path);
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'");

        AtomicFile.WithLock(() =>
        {
            var rows = ReadRows().Where(r => !string.Equals(r.Key, key, StringComparison.Ordinal)).ToList();
            rows.Add((key, canonicalPath, timestamp));
            WriteRows(rows);
        });
    }

    public static void Remove(string key)
    {
        AtomicFile.WithLock(() =>
        {
            var rows = ReadRows().Where(r => !string.Equals(r.Key, key, StringComparison.Ordinal)).ToList();
            WriteRows(rows);
        });
    }

    private static string Canonicalize(string path)
    {
        string full = Path.GetFullPath(path);
        bool isDriveRoot = full.Length == 3 && full[1] == ':' && full[2] == '\\';
        return isDriveRoot ? full : full.TrimEnd('\\', '/');
    }

    private static IEnumerable<(string Key, string Path, string LastUsedUtc)> ReadRows()
    {
        string path = DataPaths.DecisionsFile;
        if (!File.Exists(path))
            yield break;

        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            string[] fields = line.Split('\t');
            if (fields.Length != 3)
                continue; // corrupt/hand-edited line -- skip, never throw

            yield return (fields[0], fields[1], fields[2]);
        }
    }

    private static void WriteRows(IEnumerable<(string Key, string Path, string LastUsedUtc)> rows)
    {
        var lines = new List<string> { "# query\tpath\tlastUsedUtc" };
        lines.AddRange(rows.Select(r => $"{r.Key}\t{r.Path}\t{r.LastUsedUtc}"));
        AtomicFile.WriteAllLinesAtomic(DataPaths.DecisionsFile, lines);
    }
}
