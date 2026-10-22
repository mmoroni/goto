using Goto.Core;
using Goto.Tests.Fakes;

namespace Goto.Tests.Core;

public class RankerTests
{
    private static readonly IReadOnlyDictionary<string, (int, DateTime)> NoVisits =
        new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void FrecencyTerm_NeverVisited_ReturnsZero()
    {
        double score = Ranker.FrecencyTerm(@"C:\tree\documents", NoVisits, DateTime.UtcNow);
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void FrecencyTerm_HandComputedValue_MatchesLog2Formula()
    {
        DateTime now = DateTime.UtcNow;
        var visits = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\tree\documents"] = (10, now.AddMinutes(-10)), // within the hour -> factor 4
        };

        double score = Ranker.FrecencyTerm(@"C:\tree\documents", visits, now);
        double expected = 10.0 * Math.Log2(1 + 10 * 4.0);
        Assert.Equal(expected, score, precision: 10);
    }

    [Theory]
    [InlineData(-30, 4.0)]    // 30 min ago: within the hour
    [InlineData(-60, 4.0)]    // exactly 1h ago: inclusive upper bound -> still factor 4
    [InlineData(-61, 2.0)]    // just past 1h: next bucket down
    [InlineData(-1440, 2.0)]  // exactly 1d ago: inclusive -> factor 2
    [InlineData(-1441, 0.5)]  // just past 1d
    [InlineData(-10080, 0.5)] // exactly 7d ago: inclusive -> factor 0.5
    [InlineData(-10081, 0.25)] // just past 7d
    public void FrecencyTerm_RecencyBucketBoundaries_AreInclusive(int minutesAgo, double expectedFactor)
    {
        DateTime now = DateTime.UtcNow;
        var visits = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\tree\documents"] = (1, now.AddMinutes(minutesAgo)),
        };

        double score = Ranker.FrecencyTerm(@"C:\tree\documents", visits, now);
        double expected = 10.0 * Math.Log2(1 + 1 * expectedFactor);
        Assert.Equal(expected, score, precision: 6);
    }

    [Fact]
    public void Rank_PathUnderCwd_AppliesThirtyNotFifteen()
    {
        string cwd = @"C:\tree\here";
        var scores = Ranker.Rank(
            [@"C:\tree\here\documents"], NoVisits, cwd, @"C:\unrelated", DateTime.UtcNow);

        Assert.Equal(30.0, scores[0].CwdBonus);
        Assert.Equal(0.0, scores[0].ParentBonus);
    }

    [Fact]
    public void Rank_PathUnderCwdParentOnly_AppliesFifteen()
    {
        string cwd = @"C:\tree\here\nested";
        var scores = Ranker.Rank(
            [@"C:\tree\here\sibling"], NoVisits, cwd, @"C:\unrelated", DateTime.UtcNow);

        Assert.Equal(0.0, scores[0].CwdBonus);
        Assert.Equal(15.0, scores[0].ParentBonus);
    }

    [Fact]
    public void Rank_ProfileBonus_StacksIndependentlyWithCwdBonus()
    {
        string cwd = @"C:\Users\alice\tree";
        string profile = @"C:\Users\alice";
        var scores = Ranker.Rank(
            [@"C:\Users\alice\tree\documents"], NoVisits, cwd, profile, DateTime.UtcNow);

        Assert.Equal(30.0, scores[0].CwdBonus);
        Assert.Equal(15.0, scores[0].ProfileBonus);
    }

    [Fact]
    public void Rank_ProjectRoot_GitDirectory_AppliesBonus()
    {
        using var tree = new TempTree();
        string candidate = tree.CreateDir("project");
        Directory.CreateDirectory(Path.Combine(candidate, ".git"));

        var scores = Ranker.Rank([candidate], NoVisits, @"C:\elsewhere", @"C:\elsewhere", DateTime.UtcNow);

        Assert.Equal(10.0, scores[0].ProjectRootBonus);
    }

    [Fact]
    public void Rank_ProjectRoot_PackageJson_AppliesBonus()
    {
        using var tree = new TempTree();
        string candidate = tree.CreateDir("project");
        File.WriteAllText(Path.Combine(candidate, "package.json"), "{}");

        var scores = Ranker.Rank([candidate], NoVisits, @"C:\elsewhere", @"C:\elsewhere", DateTime.UtcNow);

        Assert.Equal(10.0, scores[0].ProjectRootBonus);
    }

    [Fact]
    public void Rank_ProjectRoot_CargoToml_AppliesBonus()
    {
        using var tree = new TempTree();
        string candidate = tree.CreateDir("project");
        File.WriteAllText(Path.Combine(candidate, "Cargo.toml"), "[package]");

        var scores = Ranker.Rank([candidate], NoVisits, @"C:\elsewhere", @"C:\elsewhere", DateTime.UtcNow);

        Assert.Equal(10.0, scores[0].ProjectRootBonus);
    }

    [Fact]
    public void Rank_ProjectRoot_SlnFile_AppliesBonus()
    {
        using var tree = new TempTree();
        string candidate = tree.CreateDir("project");
        File.WriteAllText(Path.Combine(candidate, "app.sln"), "");

        var scores = Ranker.Rank([candidate], NoVisits, @"C:\elsewhere", @"C:\elsewhere", DateTime.UtcNow);

        Assert.Equal(10.0, scores[0].ProjectRootBonus);
    }

    [Fact]
    public void Rank_NoProjectMarkers_NoBonus()
    {
        using var tree = new TempTree();
        string candidate = tree.CreateDir("plain");

        var scores = Ranker.Rank([candidate], NoVisits, @"C:\elsewhere", @"C:\elsewhere", DateTime.UtcNow);

        Assert.Equal(0.0, scores[0].ProjectRootBonus);
    }

    [Fact]
    public void Rank_ProjectRootBonus_NeverAppliedBeyondTop20ByPartialScore()
    {
        using var tree = new TempTree();

        // 21 candidates: 20 "decoys" with high frecency (so they dominate the partial-score
        // pre-sort), plus one with a .git folder but zero visits (so it ranks last by partial
        // score and must never receive the filesystem-touching project-root check at all).
        var visits = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();
        for (int i = 0; i < 20; i++)
        {
            string decoy = tree.CreateDir($"decoy{i}");
            candidates.Add(decoy);
            visits[decoy] = (1000, DateTime.UtcNow); // huge frecency -> always beats the git candidate
        }

        string gitCandidate = tree.CreateDir("git-project");
        Directory.CreateDirectory(Path.Combine(gitCandidate, ".git"));
        candidates.Add(gitCandidate);

        var scores = Ranker.Rank(candidates, visits, @"C:\elsewhere", @"C:\elsewhere", DateTime.UtcNow);

        ScoreBreakdown gitScore = scores.Single(s => s.Path == gitCandidate);
        Assert.Equal(0.0, gitScore.ProjectRootBonus);
    }

    [Fact]
    public void Rank_DepthPenalty_MatchesAncestorCount()
    {
        var scores = Ranker.Rank(
            [@"C:\Users\alice\Documents"], NoVisits, @"C:\elsewhere", @"C:\elsewhere", DateTime.UtcNow);

        // Ancestors (leaf excluded): ["Users", "alice"] -> depth 2 -> penalty -3.0
        Assert.Equal(-3.0, scores[0].DepthPenalty);
    }

    [Fact]
    public void Rank_FullFormula_CombinesTermsIntoHandComputedTotal()
    {
        using var tree = new TempTree();
        string candidate = tree.CreateDir("project");
        Directory.CreateDirectory(Path.Combine(candidate, ".git"));

        DateTime now = DateTime.UtcNow;
        var visits = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase)
        {
            [candidate] = (5, now.AddMinutes(-30)), // within the hour -> factor 4
        };

        var scores = Ranker.Rank([candidate], visits, candidate, @"C:\unrelated-profile", now);

        double frecency = 10.0 * Math.Log2(1 + 5 * 4.0);
        double depthPenalty = -1.5 * Matcher.GetAncestorNames(candidate).Count;
        double expectedTotal = frecency + 30.0 /* under cwd (cwd == candidate itself) */ + 0.0 + 0.0 + 10.0 + depthPenalty;

        Assert.Equal(expectedTotal, scores[0].Total, precision: 6);
    }
}
