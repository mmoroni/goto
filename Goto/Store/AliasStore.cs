namespace Goto.Store;

internal static class AliasStore
{
    public static bool TryGet(string name, out string path)
    {
        path = string.Empty;

        foreach (var row in ReadRows())
        {
            if (string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                path = row.Path;
                return true;
            }
        }

        return false;
    }

    public static void Upsert(string name, string path)
    {
        string canonicalPath = PathCanon.Canonicalize(path);

        AtomicFile.WithLock(() =>
        {
            var rows = ReadRows().Where(r => !string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            rows.Add((name, canonicalPath));
            WriteRows(rows);
        });
    }

    public static void Remove(string name)
    {
        AtomicFile.WithLock(() =>
        {
            var rows = ReadRows().Where(r => !string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            WriteRows(rows);
        });
    }

    public static IReadOnlyList<(string Name, string Path)> LoadAll() => ReadRows().ToList();

    private static IEnumerable<(string Name, string Path)> ReadRows()
    {
        string path = DataPaths.AliasesFile;
        if (!File.Exists(path))
            yield break;

        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            string[] fields = line.Split('\t');
            if (fields.Length != 2)
                continue; // corrupt/hand-edited line -- skip, never throw

            yield return (fields[0], fields[1]);
        }
    }

    private static void WriteRows(IEnumerable<(string Name, string Path)> rows)
    {
        var lines = new List<string> { "# name\tpath" };
        lines.AddRange(rows.Select(r => $"{r.Name}\t{r.Path}"));
        AtomicFile.WriteAllLinesAtomic(DataPaths.AliasesFile, lines);
    }
}
