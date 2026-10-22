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
}
