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

    [Fact]
    public void Parse_Doctor_SetsFlag()
    {
        var parsed = ArgParser.Parse(["--doctor"]);
        Assert.True(parsed.Doctor);
    }

    [Fact]
    public void Parse_DoctorWithPositionals_StillParses()
    {
        var parsed = ArgParser.Parse(["--doctor", "documents"]);
        Assert.True(parsed.Doctor);
        Assert.Equal(["documents"], parsed.Positionals);
    }

    [Fact]
    public void Parse_RecognizesExplainFlag()
    {
        var parsed = ArgParser.Parse(["--explain", "documents"]);
        Assert.True(parsed.Explain);
    }

    [Fact]
    public void Parse_RecognizesWithPromptHookFlag()
    {
        var parsed = ArgParser.Parse(["--init", "powershell", "--with-prompt-hook"]);
        Assert.True(parsed.WithPromptHook);
    }

    [Fact]
    public void Parse_AddRequiresPath_Throws()
    {
        Assert.Throws<ArgParseException>(() => ArgParser.Parse(["--add"]));
    }

    [Fact]
    public void Parse_AddWithPath_SetsAddPath()
    {
        var parsed = ArgParser.Parse(["--add", @"C:\some\folder"]);
        Assert.Equal(@"C:\some\folder", parsed.AddPath);
    }

    [Fact]
    public void Parse_AliasRequiresNameAndPath_ThrowsWithZeroOrOneValue()
    {
        Assert.Throws<ArgParseException>(() => ArgParser.Parse(["--alias"]));
        Assert.Throws<ArgParseException>(() => ArgParser.Parse(["--alias", "work"]));
    }

    [Fact]
    public void Parse_AliasWithNameAndPath_SetsBoth()
    {
        var parsed = ArgParser.Parse(["--alias", "work", @"D:\Clients\Acme"]);
        Assert.Equal("work", parsed.AliasName);
        Assert.Equal(@"D:\Clients\Acme", parsed.AliasPath);
    }

    [Fact]
    public void Parse_UnaliasRequiresName_Throws()
    {
        Assert.Throws<ArgParseException>(() => ArgParser.Parse(["--unalias"]));
    }

    [Fact]
    public void Parse_UnaliasWithName_SetsUnaliasName()
    {
        var parsed = ArgParser.Parse(["--unalias", "work"]);
        Assert.Equal("work", parsed.UnaliasName);
    }

    [Fact]
    public void Parse_Aliases_SetsFlag()
    {
        var parsed = ArgParser.Parse(["--aliases"]);
        Assert.True(parsed.ListAliases);
    }

    [Fact]
    public void Parse_CompleteWithEmptyString_IsDistinctFromAbsent()
    {
        var withEmpty = ArgParser.Parse(["--complete", ""]);
        var absent = ArgParser.Parse(["documents"]);

        Assert.Equal("", withEmpty.CompleteWord);
        Assert.Null(absent.CompleteWord);
    }

    [Fact]
    public void Parse_CompleteRequiresArgument_Throws()
    {
        Assert.Throws<ArgParseException>(() => ArgParser.Parse(["--complete"]));
    }

    [Fact]
    public void Parse_CompleteEqualsForm_SetsCompleteWord()
    {
        var parsed = ArgParser.Parse(["--complete=Do"]);
        Assert.Equal("Do", parsed.CompleteWord);
    }

    [Fact]
    public void Parse_CompleteEqualsForm_EmptyWord_IsNeverElided()
    {
        // The whole "--complete=" token is non-empty even when the word part after '=' is --
        // this is the real fix for PowerShell dropping a literal empty-string argv element.
        var parsed = ArgParser.Parse(["--complete="]);
        Assert.Equal("", parsed.CompleteWord);
    }

    [Fact]
    public void Parse_Wrong_SetsFlag()
    {
        var parsed = ArgParser.Parse(["--wrong"]);
        Assert.True(parsed.Wrong);
    }

    [Fact]
    public void Parse_ForgetCollectsAllFollowingTerms()
    {
        var parsed = ArgParser.Parse(["--forget", "a", "b", "c"]);
        Assert.Equal(["a", "b", "c"], parsed.ForgetTerms);
    }

    [Fact]
    public void Parse_ForgetWithNothingAfter_Throws()
    {
        Assert.Throws<ArgParseException>(() => ArgParser.Parse(["--forget"]));
    }

    [Fact]
    public void Parse_ForgetTreatsDoubleDashAsToggleNotStop()
    {
        var parsed = ArgParser.Parse(["--forget", "a", "--", "-b"]);
        Assert.Equal(["a", "-b"], parsed.ForgetTerms);
    }

    [Theory]
    [InlineData("-e")]
    [InlineData("--explore")]
    public void Parse_RecognizesExploreFlag(string flag)
    {
        var parsed = ArgParser.Parse([flag, "documents"]);
        Assert.True(parsed.Explore);
        Assert.False(parsed.Code);
    }

    [Theory]
    [InlineData("-c")]
    [InlineData("--code")]
    public void Parse_RecognizesCodeFlag(string flag)
    {
        var parsed = ArgParser.Parse([flag, "documents"]);
        Assert.True(parsed.Code);
        Assert.False(parsed.Explore);
    }

    [Fact]
    public void Parse_ExploreAndCodeTogether_Throws()
    {
        Assert.Throws<ArgParseException>(() => ArgParser.Parse(["-e", "-c", "documents"]));
    }

    [Fact]
    public void Parse_ListAndExploreTogether_Throws()
    {
        Assert.Throws<ArgParseException>(() => ArgParser.Parse(["--list", "-e", "documents"]));
    }

    [Fact]
    public void Parse_ListAndCodeTogether_Throws()
    {
        Assert.Throws<ArgParseException>(() => ArgParser.Parse(["--list", "-c", "documents"]));
    }

    [Fact]
    public void Parse_PickAndExploreTogether_DoesNotThrow()
    {
        var parsed = ArgParser.Parse(["--pick", "-e", "documents"]);
        Assert.True(parsed.Pick);
        Assert.True(parsed.Explore);
    }

    [Fact]
    public void Parse_PickAndCodeTogether_DoesNotThrow()
    {
        var parsed = ArgParser.Parse(["--pick", "-c", "documents"]);
        Assert.True(parsed.Pick);
        Assert.True(parsed.Code);
    }

    [Fact]
    public void Parse_History_SetsFlag()
    {
        var parsed = ArgParser.Parse(["--history"]);
        Assert.True(parsed.History);
    }

    [Fact]
    public void Parse_HistoryWithPositionals_StillParses()
    {
        var parsed = ArgParser.Parse(["--history", "documents"]);
        Assert.True(parsed.History);
        Assert.Equal(["documents"], parsed.Positionals);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Parse_RecognizesHelpFlag(string flag)
    {
        var parsed = ArgParser.Parse([flag]);
        Assert.True(parsed.Help);
    }
}
