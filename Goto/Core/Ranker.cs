using Goto.Store;

namespace Goto.Core;

internal readonly record struct ScoreBreakdown(
    string Path,
    double FrecencyTerm,
    double CwdBonus,
    double ParentBonus,
    double ProfileBonus,
    double ProjectRootBonus,
    double DepthPenalty,
    double Total);

internal static class Ranker
{
    private const double FrecencyWeight = 10.0;
    private const double CwdBonusValue = 30.0;
    private const double ParentBonusValue = 15.0;
    private const double ProfileBonusValue = 15.0;
    private const double ProjectRootBonusValue = 10.0;
    private const double DepthPenaltyPerLevel = 1.5;
    private const int ProjectRootCheckTopN = 20;

    public static ScoreBreakdown[] Rank(
        IReadOnlyList<string> candidates,
        IReadOnlyDictionary<string, (int VisitCount, DateTime LastVisitUtc)> visits,
        string cwd,
        string userProfile,
        DateTime nowUtc)
    {
        // Phase 1: every term except project-root, for ALL candidates. No filesystem I/O.
        var partial = new (string Path, double Frecency, double Cwd, double Parent, double Profile, double Depth)[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            string path = candidates[i];
            double frecency = FrecencyTerm(path, visits, nowUtc);
            (double cwdB, double parentB) = LocalityCwdParent(path, cwd);
            double profileB = IsUnderOrEqual(path, userProfile) ? ProfileBonusValue : 0.0;
            double depthPenalty = -DepthPenaltyPerLevel * Depth(path);
            partial[i] = (path, frecency, cwdB, parentB, profileB, depthPenalty);
        }

        // Phase 2: cheap partial score picks the top 20 -- ONLY those pay the
        // filesystem-touching project-root check (spec: "check top 20 only"). This is an
        // accepted approximation: a candidate ranked #21 by partial score that happens to
        // have a .git folder never gets its +10 and can't out-rank #20.
        double PartialScore((string Path, double Frecency, double Cwd, double Parent, double Profile, double Depth) p) =>
            p.Frecency + p.Cwd + p.Parent + p.Profile + p.Depth;

        var top20 = partial
            .OrderByDescending(PartialScore)
            .Take(ProjectRootCheckTopN)
            .Select(p => p.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new ScoreBreakdown[partial.Length];
        for (int i = 0; i < partial.Length; i++)
        {
            var p = partial[i];
            double projectRoot = top20.Contains(p.Path) && LooksLikeProjectRoot(p.Path)
                ? ProjectRootBonusValue
                : 0.0;
            double total = p.Frecency + p.Cwd + p.Parent + p.Profile + projectRoot + p.Depth;
            result[i] = new ScoreBreakdown(p.Path, p.Frecency, p.Cwd, p.Parent, p.Profile, projectRoot, p.Depth, total);
        }

        return result; // caller sorts descending by Total
    }

    internal static double FrecencyTerm(
        string path,
        IReadOnlyDictionary<string, (int VisitCount, DateTime LastVisitUtc)> visits,
        DateTime nowUtc)
    {
        if (!visits.TryGetValue(PathCanon.Canonicalize(path), out var v) || v.VisitCount <= 0)
            return 0.0; // log2(1 + 0 * anything) = log2(1) = 0 -- no special case needed

        double recencyFactor = RecencyFactor(nowUtc - v.LastVisitUtc);
        return FrecencyWeight * Math.Log2(1 + v.VisitCount * recencyFactor);
    }

    private static double RecencyFactor(TimeSpan age) =>
        age <= TimeSpan.FromHours(1) ? 4.0 :
        age <= TimeSpan.FromDays(1) ? 2.0 :
        age <= TimeSpan.FromDays(7) ? 0.5 : 0.25; // inclusive upper bounds at each boundary

    private static (double Cwd, double Parent) LocalityCwdParent(string path, string cwd)
    {
        // +30 under cwd, ELIF +15 under cwd's parent -- mutually exclusive per spec wording.
        if (IsUnderOrEqual(path, cwd))
            return (CwdBonusValue, 0.0);

        string? parent = Path.GetDirectoryName(cwd.TrimEnd('\\', '/'));
        if (!string.IsNullOrEmpty(parent) && IsUnderOrEqual(path, parent))
            return (0.0, ParentBonusValue);

        return (0.0, 0.0);
    }

    // NOT mutually exclusive with the above: a path can be under cwd AND %USERPROFILE% at
    // once (extremely common -- cwd usually lives under the profile), so both can stack.
    private static bool IsUnderOrEqual(string path, string root)
    {
        string normRoot = root.TrimEnd('\\', '/');
        if (normRoot.Length == 0)
            return false;

        return string.Equals(path, normRoot, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normRoot + "\\", StringComparison.OrdinalIgnoreCase);
    }

    // Ancestor segment count, reusing Matcher.GetAncestorNames (already shared with
    // Suggestions.cs) -- e.g. C:\Users\alice\Documents -> ["Users","alice"] -> depth 2.
    private static int Depth(string path) => Matcher.GetAncestorNames(path).Count;

    // Checks the CANDIDATE folder itself, not whether it's a child of a project root.
    private static bool LooksLikeProjectRoot(string path)
    {
        try
        {
            if (Directory.Exists(Path.Combine(path, ".git")))
                return true;
            if (File.Exists(Path.Combine(path, "package.json")))
                return true;
            if (File.Exists(Path.Combine(path, "Cargo.toml")))
                return true;
            return Directory.Exists(path) && Directory.EnumerateFiles(path, "*.sln").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
