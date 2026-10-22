using Goto.Core;

namespace Goto.Tests.Core;

public class EditDistanceTests
{
    [Fact]
    public void Levenshtein_IdenticalStrings_ReturnsZero()
    {
        Assert.Equal(0, EditDistance.Levenshtein("documents", "documents"));
    }

    [Fact]
    public void Levenshtein_SingleSubstitution_ReturnsOne()
    {
        Assert.Equal(1, EditDistance.Levenshtein("documents", "documants"));
    }

    [Fact]
    public void Levenshtein_SingleInsertion_ReturnsOne()
    {
        // "documennts" has one extra 'n' compared to "documents"
        Assert.Equal(1, EditDistance.Levenshtein("documents", "documennts"));
    }

    [Fact]
    public void Levenshtein_SingleDeletion_ReturnsOne()
    {
        Assert.Equal(1, EditDistance.Levenshtein("documents", "dcuments"));
    }

    [Fact]
    public void Levenshtein_IsCaseInsensitive()
    {
        Assert.Equal(0, EditDistance.Levenshtein("DOCUMENTS", "documents"));
    }

    [Fact]
    public void Levenshtein_FarApartStrings_ReportsDistanceAboveTwo()
    {
        Assert.True(EditDistance.Levenshtein("documents", "zzzzzzzzz") > 2);
    }
}
