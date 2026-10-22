using Goto.Shell;

namespace Goto.Tests.Shell;

public class ProfileInstallBlockTests
{
    [Fact]
    public void Build_WithoutPromptHook_ContainsMarkersAndPlainInit()
    {
        string block = ProfileInstallBlock.Build(@"C:\tools\goto.exe", withPromptHook: false);

        Assert.Contains(ProfileInstallBlock.BeginMarker, block);
        Assert.Contains(ProfileInstallBlock.EndMarker, block);
        Assert.Contains("--init powershell", block);
        Assert.DoesNotContain("--with-prompt-hook", block);
        Assert.Contains(@"C:\tools\goto.exe", block);
    }

    [Fact]
    public void Build_WithPromptHook_IncludesHookFlag()
    {
        string block = ProfileInstallBlock.Build(@"C:\tools\goto.exe", withPromptHook: true);

        Assert.Contains("--init powershell --with-prompt-hook", block);
    }

    [Fact]
    public void Build_BeginMarkerComesBeforeEndMarker()
    {
        string block = ProfileInstallBlock.Build(@"C:\tools\goto.exe", withPromptHook: false);

        int beginIndex = block.IndexOf(ProfileInstallBlock.BeginMarker, StringComparison.Ordinal);
        int endIndex = block.IndexOf(ProfileInstallBlock.EndMarker, StringComparison.Ordinal);

        Assert.True(beginIndex >= 0 && endIndex > beginIndex);
    }

    [Fact]
    public void Build_PipesThroughOutStringBeforeInvokeExpression()
    {
        // Regression test for a real bug: piping a captured native process's multi-line stdout
        // directly into Invoke-Expression invokes it once PER LINE (Invoke-Expression binds
        // -Command from the pipeline), not once for the whole script -- breaking on blank lines
        // and on any construct spanning multiple lines (e.g. `function goto { ... }`). Out-String
        // must join the array back into one string first. See
        // docs/specs/addendum-02-stdout-discipline-bugfixes.md.
        string block = ProfileInstallBlock.Build(@"C:\tools\goto.exe", withPromptHook: false);

        Assert.Contains("| Out-String) | Invoke-Expression", block);
    }
}
