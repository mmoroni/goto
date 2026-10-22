using Goto.Search;

namespace Goto.Tests.Search;

public class EverythingQueryBuilderTests
{
    [Fact]
    public void Build_ExactMode_ProducesWfnQuery()
    {
        string query = EverythingQueryBuilder.Build("documents", [], MatchMode.Exact);
        Assert.Equal("folder: wfn:\"documents\"", query);
    }

    [Fact]
    public void Build_PrefixMode_ProducesStartwithQuery()
    {
        string query = EverythingQueryBuilder.Build("docu", [], MatchMode.Prefix);
        Assert.Equal("folder: startwith:\"docu\"", query);
    }

    [Fact]
    public void Build_ContainsMode_ProducesPlainFolderQuery()
    {
        string query = EverythingQueryBuilder.Build("docu", [], MatchMode.Contains);
        Assert.Equal("folder: \"docu\"", query);
    }

    [Fact]
    public void Build_WithQualifiers_AppendsPathFilters()
    {
        string query = EverythingQueryBuilder.Build("documents", ["user", "alice"], MatchMode.Exact);
        Assert.Equal("folder: wfn:\"documents\" path:\"user\" path:\"alice\"", query);
    }

    [Fact]
    public void Quote_StripsEmbeddedDoubleQuotes()
    {
        Assert.Equal("\"abc\"", EverythingQueryBuilder.Quote("a\"b\"c"));
    }

    [Fact]
    public void Quote_PreservesWildcards()
    {
        Assert.Equal("\"doc*s?\"", EverythingQueryBuilder.Quote("doc*s?"));
    }

    [Fact]
    public void Build_UnsupportedMode_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EverythingQueryBuilder.Build("documents", [], MatchMode.Abbreviation));
    }
}
