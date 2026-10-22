namespace Goto.Store;

internal static class VisitStore
{
    public static void Increment(string path)
    {
        string canonicalPath = Canonicalize(path);
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'");

        AtomicFile.WithLock(() =>
        {
            var rows = ReadRows().ToList();
            int existingIndex = rows.FindIndex(r => string.Equals(r.Path, canonicalPath, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                var existing = rows[existingIndex];
                rows[existingIndex] = (existing.Path, existing.VisitCount + 1, timestamp);
            }
            else
            {
                rows.Add((canonicalPath, 1, timestamp));
            }

            WriteRows(rows);
        });
    }

    private static string Canonicalize(string path)
    {
        string full = Path.GetFullPath(path);
        bool isDriveRoot = full.Length == 3 && full[1] == ':' && full[2] == '\\';
        return isDriveRoot ? full : full.TrimEnd('\\', '/');
    }

    private static IEnumerable<(string Path, int VisitCount, string LastVisitUtc)> ReadRows()
    {
        string path = DataPaths.VisitsFile;
        if (!File.Exists(path))
            yield break;

        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            string[] fields = line.Split('\t');
            if (fields.Length != 3 || !int.TryParse(fields[1], out int count))
                continue; // corrupt/hand-edited line -- skip, never throw

            yield return (fields[0], count, fields[2]);
        }
    }

    private static void WriteRows(IEnumerable<(string Path, int VisitCount, string LastVisitUtc)> rows)
    {
        var lines = new List<string> { "# path\tvisitCount\tlastVisitUtc" };
        lines.AddRange(rows.Select(r => $"{r.Path}\t{r.VisitCount}\t{r.LastVisitUtc}"));
        AtomicFile.WriteAllLinesAtomic(DataPaths.VisitsFile, lines);
    }
}
