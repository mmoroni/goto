using System.Globalization;

namespace Goto.Store;

internal static class VisitStore
{
    private const int AgingThreshold = 10_000;
    private const double AgingFactor = 0.9;
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss'Z'";

    public static void Increment(string path)
    {
        string canonicalPath = PathCanon.Canonicalize(path);
        string timestamp = DateTime.UtcNow.ToString(TimestampFormat);

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

            rows = ApplyAgingIfNeeded(rows);
            WriteRows(rows);
        });
    }

    /// <summary>
    /// Bulk-loads every row once, for Ranker's per-candidate lookups across a whole
    /// Resolve call (one file scan beats up to 2000 individual reads).
    /// </summary>
    public static IReadOnlyDictionary<string, (int VisitCount, DateTime LastVisitUtc)> LoadAll()
    {
        var result = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in ReadRows())
        {
            if (DateTime.TryParseExact(
                    row.LastVisitUtc, TimestampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime dt))
            {
                result[row.Path] = (row.VisitCount, dt);
            }
            // else: corrupt/hand-edited timestamp -- skip the row, never throw.
        }

        return result;
    }

    private static List<(string Path, int VisitCount, string LastVisitUtc)> ApplyAgingIfNeeded(
        List<(string Path, int VisitCount, string LastVisitUtc)> rows)
    {
        long total = rows.Sum(r => (long)r.VisitCount);
        if (total <= AgingThreshold)
            return rows;

        return rows
            .Select(r => (r.Path, VisitCount: (int)(r.VisitCount * AgingFactor), r.LastVisitUtc))
            .Where(r => r.VisitCount >= 1)
            .ToList();
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
