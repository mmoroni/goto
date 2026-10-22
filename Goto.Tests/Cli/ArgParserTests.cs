using Goto.Cli;

namespace Goto.Tests.Cli;

public class ArgParserTests
{
    [Theory]
    [InlineData("-l")]
    [InlineData("--list")]
    public void Parse_RecognizesListFlag(string flag)
    {
        var parsed = ArgParser.Parse([flag, "documents"]);
        Assert.True(parsed.List);
        Assert.False(parsed.Pick);
    }

    [Theory]
    [InlineData("-p")]
    [InlineData("--pick")]
    public void Parse_RecognizesPickFlag(string flag)
    {
        var parsed = ArgParser.Parse([flag, "documents"]);
        Assert.True(parsed.Pick);
        Assert.False(parsed.List);
    }

    [Fact]
    public void Parse_RecognizesNoUiFlag()
    {
        var parsed = ArgParser.Parse(["--no-ui", "documents"]);
        Assert.True(parsed.NoUi);
    }

    [Fact]
    public void Parse_ListAndPickTogether_Throws()
    {
        Assert.Throws<ArgParseException>(() => ArgParser.Parse(["--list", "--pick", "documents"]));
    }

    [Fact]
    public void Parse_ListAndNoUiTogether_DoesNotThrow()
    {
        var parsed = ArgParser.Parse(["--list", "--no-ui", "documents"]);
        Assert.True(parsed.List);
        Assert.True(parsed.NoUi);
    }
}
