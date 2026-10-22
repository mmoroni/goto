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
}
