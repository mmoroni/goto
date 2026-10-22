using Goto.Core;

namespace Goto.Tests.Core;

public class SuggestionsTests
{
    [Fact]
    public void Suggest_FewerThanTwoCandidates_ReturnsEmpty()
    {
        var result = Suggestions.Suggest([@"C:\tree\documents"], "documents");
        Assert.Empty(result);
    }

    [Fact]
    public void Suggest_AncestorInSomeButNotAll_IsSuggested()
    {
        var candidates = new[] { @"C:\tree\alice\documents", @"C:\tree\work\documents" };
        var result = Suggestions.Suggest(candidates, "documents");

        Assert.Contains("goto alice documents", result);
        Assert.Contains("goto work documents", result);
    }

    [Fact]
    public void Suggest_AncestorCommonToAll_IsNotSuggested()
    {
        var candidates = new[] { @"C:\tree\alice\documents", @"C:\tree\work\documents" };
        var result = Suggestions.Suggest(candidates, "documents");

        // "tree" is an ancestor of every candidate -- doesn't narrow anything, must not appear.
        Assert.DoesNotContain(result, s => s.Contains("tree", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Suggest_CapsAtThreeSuggestions()
    {
        var candidates = new[]
        {
            @"C:\tree\alpha\documents",
            @"C:\tree\beta\documents",
            @"C:\tree\gamma\documents",
            @"C:\tree\delta\documents",
            @"C:\tree\epsilon\documents",
        };
        var result = Suggestions.Suggest(candidates, "documents");

        Assert.True(result.Count <= 3);
    }

    [Fact]
    public void Suggest_TieBreaksAlphabetically()
    {
        // Two single-candidate-only ancestors, equally "evenly splitting" (1 of 2) -- alphabetical order.
        var candidates = new[] { @"C:\tree\zeta\documents", @"C:\tree\alpha\documents" };
        var result = Suggestions.Suggest(candidates, "documents");

        Assert.Equal("goto alpha documents", result[0]);
        Assert.Equal("goto zeta documents", result[1]);
    }
}
