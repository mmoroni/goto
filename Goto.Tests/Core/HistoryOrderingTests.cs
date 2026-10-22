using Goto.Core;

namespace Goto.Tests.Core;

public class HistoryOrderingTests
{
    [Fact]
    public void OrderByRecency_EmptyInput_ReturnsEmpty()
    {
        var visits = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase);

        var ordered = HistoryOrdering.OrderByRecency(visits);

        Assert.Empty(ordered);
    }

    [Fact]
    public void OrderByRecency_SortsByMostRecentFirst()
    {
        DateTime now = DateTime.UtcNow;
        var visits = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\old"] = (5, now.AddDays(-5)),
            [@"C:\new"] = (1, now.AddMinutes(-1)),
            [@"C:\mid"] = (3, now.AddHours(-1)),
        };

        var ordered = HistoryOrdering.OrderByRecency(visits);

        Assert.Equal([@"C:\new", @"C:\mid", @"C:\old"], ordered);
    }

    [Fact]
    public void OrderByRecency_TiesOnTimestamp_BreakByHigherVisitCount()
    {
        DateTime sameTime = DateTime.UtcNow;
        var visits = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\low"] = (2, sameTime),
            [@"C:\high"] = (9, sameTime),
        };

        var ordered = HistoryOrdering.OrderByRecency(visits);

        Assert.Equal([@"C:\high", @"C:\low"], ordered);
    }
}
