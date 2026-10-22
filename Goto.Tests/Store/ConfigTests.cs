using Goto.Store;
using Goto.Tests.Fakes;

namespace Goto.Tests.Store;

public class ConfigTests
{
    [Fact]
    public void Load_NoConfigFile_DefaultsMaxCandidatesTo15()
    {
        using var home = new TempGotoHome();
        var config = Config.Load();
        Assert.Equal(15, config.MaxCandidates);
    }

    [Fact]
    public void Load_WithMaxCandidatesLine_ParsesIt()
    {
        using var home = new TempGotoHome();
        File.WriteAllText(Path.Combine(home.Path, "config.txt"), "max_candidates = 7");

        var config = Config.Load();

        Assert.Equal(7, config.MaxCandidates);
    }

    [Theory]
    [InlineData("max_candidates = abc")]
    [InlineData("max_candidates = -5")]
    [InlineData("max_candidates = 0")]
    public void Load_WithInvalidMaxCandidatesValue_KeepsDefault(string line)
    {
        using var home = new TempGotoHome();
        File.WriteAllText(Path.Combine(home.Path, "config.txt"), line);

        var config = Config.Load();

        Assert.Equal(15, config.MaxCandidates);
    }

    [Fact]
    public void Load_NoConfigFile_DefaultsBackendToAuto()
    {
        using var home = new TempGotoHome();
        var config = Config.Load();
        Assert.Equal(BackendMode.Auto, config.Backend);
    }

    [Theory]
    [InlineData("backend = walk", "Walk")]
    [InlineData("backend = everything", "Everything")]
    [InlineData("backend = auto", "Auto")]
    [InlineData("backend = WALK", "Walk")]
    public void Load_BackendLine_ParsesItCaseInsensitively(string line, string expectedName)
    {
        using var home = new TempGotoHome();
        File.WriteAllText(Path.Combine(home.Path, "config.txt"), line);

        var config = Config.Load();

        Assert.Equal(expectedName, config.Backend.ToString());
    }

    [Fact]
    public void Load_InvalidBackendValue_KeepsAutoDefault()
    {
        using var home = new TempGotoHome();
        File.WriteAllText(Path.Combine(home.Path, "config.txt"), "backend = nonsense");

        var config = Config.Load();

        Assert.Equal(BackendMode.Auto, config.Backend);
    }

    [Fact]
    public void Load_EverythingPathLine_ParsesIt()
    {
        using var home = new TempGotoHome();
        File.WriteAllText(Path.Combine(home.Path, "config.txt"), @"everything_path = C:\Tools\Everything.exe");

        var config = Config.Load();

        Assert.Equal(@"C:\Tools\Everything.exe", config.EverythingPath);
    }

    [Fact]
    public void Load_BlankEverythingPath_IsNull()
    {
        using var home = new TempGotoHome();
        File.WriteAllText(Path.Combine(home.Path, "config.txt"), "everything_path = ");

        var config = Config.Load();

        Assert.Null(config.EverythingPath);
    }
}
