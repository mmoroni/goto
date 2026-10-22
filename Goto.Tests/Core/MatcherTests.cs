using Goto.Core;
using Goto.Search;

namespace Goto.Tests.Core;

public class MatcherTests
{
    [Fact]
    public void ExactLeaf_WithQualifierInAncestor_Matches()
    {
        // Spec worked example: `goto user documents` vs C:\Users\alice\Documents
        var query = GotoQuery.FromPositionals(["user", "documents"]);

        Assert.True(Matcher.IsMatch(@"C:\Users\alice\Documents", query, MatchMode.Exact));
    }

    [Fact]
    public void ExactLeaf_WithoutQualifierInAncestor_DoesNotMatch()
    {
        var query = GotoQuery.FromPositionals(["user", "documents"]);

        Assert.False(Matcher.IsMatch(@"C:\Projects\Documents", query, MatchMode.Exact));
    }

    [Fact]
    public void ContainsOnlyLeaf_FailsExactAndPrefix_SucceedsContains()
    {
        var query = GotoQuery.FromPositionals(["documents"]);

        Assert.False(Matcher.IsMatch(@"C:\tree\my-documents", query, MatchMode.Exact));
        Assert.False(Matcher.IsMatch(@"C:\tree\my-documents", query, MatchMode.Prefix));
        Assert.True(Matcher.IsMatch(@"C:\tree\my-documents", query, MatchMode.Contains));
    }

    [Fact]
    public void Qualifiers_MustMatchInLeftToRightOrder()
    {
        var query = GotoQuery.FromPositionals(["b", "a", "leaf"]);

        // ancestors are [a, b] root-to-leaf -- "b" then "a" is out of order, must fail
        Assert.False(Matcher.IsMatch(@"C:\a\b\leaf", query, MatchMode.Exact));
    }

    [Fact]
    public void Qualifiers_EachClaimsADistinctAncestor_NoReuse()
    {
        var query = GotoQuery.FromPositionals(["a", "a", "leaf"]);

        // only one ancestor named "a" -- second qualifier can't reuse it
        Assert.False(Matcher.IsMatch(@"C:\a\other\leaf", query, MatchMode.Exact));
    }

    [Fact]
    public void NoQualifiers_OnlyLeafMustMatch()
    {
        var query = GotoQuery.FromPositionals(["documents"]);

        Assert.True(Matcher.IsMatch(@"C:\anything\Documents", query, MatchMode.Exact));
    }

    [Fact]
    public void Abbreviation_DlMatchesDownloads()
    {
        var query = GotoQuery.FromPositionals(["dl"]);

        Assert.True(Matcher.IsMatch(@"C:\Users\alice\Downloads", query, MatchMode.Abbreviation));
    }

    [Fact]
    public void Abbreviation_DocMatchesDocuments()
    {
        // Spec's own "d*o*c*" notation, worked literally: d anchors, o at index 1, c at index 2.
        var query = GotoQuery.FromPositionals(["doc"]);

        Assert.True(Matcher.IsMatch(@"C:\tree\documents", query, MatchMode.Abbreviation));
    }

    [Fact]
    public void Abbreviation_FirstCharMismatch_Fails()
    {
        var query = GotoQuery.FromPositionals(["xl"]);

        Assert.False(Matcher.IsMatch(@"C:\Users\alice\Downloads", query, MatchMode.Abbreviation));
    }

    [Fact]
    public void Abbreviation_OutOfOrderCharacters_Fails()
    {
        var query = GotoQuery.FromPositionals(["abc"]);

        // leaf "acb": after anchoring 'a' and matching 'b' at index 2, 'c' is searched starting
        // at index 3 -- past the end of a 3-character leaf -- so order is genuinely enforced.
        Assert.False(Matcher.IsMatch(@"C:\tree\acb", query, MatchMode.Abbreviation));
    }

    [Fact]
    public void Abbreviation_WithQualifier_StillAppliesAncestorCheck()
    {
        var query = GotoQuery.FromPositionals(["user", "dl"]);

        Assert.True(Matcher.IsMatch(@"C:\Users\alice\Downloads", query, MatchMode.Abbreviation));
        Assert.False(Matcher.IsMatch(@"C:\Projects\Downloads", query, MatchMode.Abbreviation));
    }
}
