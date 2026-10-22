using System.Globalization;

namespace Goto.Store;

internal sealed class Config
{
    private static readonly string[] DefaultExclude =
    [
        "node_modules", "$Recycle.Bin", "System Volume Information", "WinSxS", ".git", "AppData\\Local\\Temp"
    ];

    public IReadOnlyList<string> Exclude { get; internal init; } = DefaultExclude;
    public int SearchTimeoutMs { get; internal init; } = 3000;
    public int MaxCandidates { get; internal init; } = 15;
    public string? EverythingPath { get; internal init; }
    public double DominanceMargin { get; internal init; } = 15.0;

    public static Config Load()
    {
        string path = DataPaths.ConfigFile;
        if (!File.Exists(path))
            return new Config();

        var exclude = new List<string>();
        int timeoutMs = 3000;
        int maxCandidates = 15;
        string? everythingPath = null;
        double dominanceMargin = 15.0;

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;

            int eq = line.IndexOf('=');
            if (eq < 0)
                continue; // malformed -- skip, never throw

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "exclude":
                    exclude.AddRange(SplitList(value));
                    break;
                case "search_timeout_ms":
                    if (int.TryParse(value, out int ms) && ms > 0)
                        timeoutMs = ms;
                    break;
                case "max_candidates":
                    if (int.TryParse(value, out int mc) && mc > 0)
                        maxCandidates = mc;
                    break;
                case "everything_path":
                    everythingPath = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "dominance_margin":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dm) && dm >= 0)
                        dominanceMargin = dm;
                    break;
            }
        }

        return new Config
        {
            Exclude = exclude.Count > 0 ? exclude : DefaultExclude,
            SearchTimeoutMs = timeoutMs,
            MaxCandidates = maxCandidates,
            EverythingPath = everythingPath,
            DominanceMargin = dominanceMargin,
        };
    }

    public bool IsExcluded(string path) =>
        Exclude.Any(ex => path.Contains(ex, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> SplitList(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#');
        return hash >= 0 ? line[..hash] : line;
    }
}
