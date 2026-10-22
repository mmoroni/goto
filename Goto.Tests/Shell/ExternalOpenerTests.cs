using Goto.Shell;

namespace Goto.Tests.Shell;

public class ExternalOpenerTests
{
    [Fact]
    public void BuildStartInfo_Explorer_LaunchesExplorerExeWithoutShellExecute()
    {
        var psi = ExternalOpener.BuildStartInfo(OpenTarget.Explorer, @"C:\some\folder");

        Assert.Equal("explorer.exe", psi.FileName);
        Assert.False(psi.UseShellExecute);
        Assert.Equal([@"C:\some\folder"], psi.ArgumentList);
    }

    [Fact]
    public void BuildStartInfo_Code_LaunchesCodeViaShellExecute()
    {
        var psi = ExternalOpener.BuildStartInfo(OpenTarget.Code, @"C:\some\folder");

        Assert.Equal("code", psi.FileName);
        Assert.True(psi.UseShellExecute);
        Assert.Equal([@"C:\some\folder"], psi.ArgumentList);
    }
}
