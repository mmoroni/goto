using Goto.Store;
using Goto.Tests.Fakes;

namespace Goto.Tests.Store;

public class VisitStoreTests
{
    [Fact]
    public void LoadAll_EmptyStore_ReturnsEmptyDictionary()
    {
        using var home = new TempGotoHome();
        var visits = VisitStore.LoadAll();
        Assert.Empty(visits);
    }

    [Fact]
    public void LoadAll_RoundTripsIncrementedVisit()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string target = tree.CreateDir("documents");

        VisitStore.Increment(target);

        var visits = VisitStore.LoadAll();
        Assert.True(visits.TryGetValue(target, out var v));
        Assert.Equal(1, v.VisitCount);
    }

    [Fact]
    public void LoadAll_CorruptTimestamp_SkipsRow()
    {
        using var home = new TempGotoHome();
        // Valid path and visit count, but an unparseable timestamp -- LoadAll's own
        // DateTime.TryParseExact guard (distinct from ReadRows' int.TryParse guard) must
        // skip this row rather than throw.
        File.WriteAllText(DataPaths.VisitsFile, "C:\\some\\path\t5\tnot-a-timestamp\n");

        var visits = VisitStore.LoadAll();

        Assert.Empty(visits);
    }

    [Fact]
    public void Increment_SumBelowThreshold_NoAgingApplied()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string seeded = tree.CreateDir("seeded");
        string other = tree.CreateDir("other");

        File.WriteAllLines(DataPaths.VisitsFile,
        [
            "# path\tvisitCount\tlastVisitUtc",
            $"{seeded}\t50\t2026-01-01T00:00:00Z",
        ]);

        VisitStore.Increment(other);

        var visits = VisitStore.LoadAll();
        Assert.Equal(50, visits[seeded].VisitCount);
        Assert.Equal(1, visits[other].VisitCount);
    }

    [Fact]
    public void Increment_SumExceedsThreshold_AgesAllRowsByPointNine()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string seeded = tree.CreateDir("seeded");
        string pusher = tree.CreateDir("pusher");

        // Seeded total (9995) is just under the 10,000 threshold; incrementing a fresh
        // path (pusher, +1) pushes the sum to 9996 -- still under, so bump the seed higher
        // to guarantee crossing it after the +1.
        File.WriteAllLines(DataPaths.VisitsFile,
        [
            "# path\tvisitCount\tlastVisitUtc",
            $"{seeded}\t10000\t2026-01-01T00:00:00Z",
        ]);

        VisitStore.Increment(pusher);

        var visits = VisitStore.LoadAll();
        Assert.Equal((int)(10000 * 0.9), visits[seeded].VisitCount);
    }

    [Fact]
    public void Increment_AgingDropsSubOneEntries()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string tiny = tree.CreateDir("tiny");
        string big = tree.CreateDir("big");
        string pusher = tree.CreateDir("pusher");

        File.WriteAllLines(DataPaths.VisitsFile,
        [
            "# path\tvisitCount\tlastVisitUtc",
            $"{tiny}\t1\t2026-01-01T00:00:00Z",
            $"{big}\t10000\t2026-01-01T00:00:00Z",
        ]);

        VisitStore.Increment(pusher);

        var visits = VisitStore.LoadAll();
        Assert.False(visits.ContainsKey(tiny), "a count of 1 truncates to 0 after aging and must be dropped");
    }

    [Fact]
    public void Increment_FreshlyAddedRowCanBeImmediatelyAgedAwayInSameCall()
    {
        using var home = new TempGotoHome();
        using var tree = new TempTree();
        string big = tree.CreateDir("big");
        string freshlyAdded = tree.CreateDir("fresh");

        // Seeding exactly at the threshold means adding the brand-new "fresh" row (count 1)
        // is itself what tips the sum over -- and aging can then immediately truncate that
        // same fresh row's count (1 * 0.9 = 0) back out in the very call that created it.
        File.WriteAllLines(DataPaths.VisitsFile,
        [
            "# path\tvisitCount\tlastVisitUtc",
            $"{big}\t10000\t2026-01-01T00:00:00Z",
        ]);

        VisitStore.Increment(freshlyAdded);

        var visits = VisitStore.LoadAll();
        Assert.False(visits.ContainsKey(freshlyAdded));
    }
}
