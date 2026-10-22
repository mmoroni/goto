namespace Goto.Tests.Fakes;

/// <summary>
/// Points GOTO_HOME at a fresh temp directory for the duration of a test and restores the
/// previous value on disposal. Tests run non-parallel (see AssemblyInfo.cs) since this mutates
/// a process-wide environment variable.
/// </summary>
internal sealed class TempGotoHome : IDisposable
{
    private readonly string? _previous;

    public string Path { get; }

    public TempGotoHome()
    {
        _previous = Environment.GetEnvironmentVariable("GOTO_HOME");
        Path = Directory.CreateTempSubdirectory("goto-test-home-").FullName;
        Environment.SetEnvironmentVariable("GOTO_HOME", Path);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GOTO_HOME", _previous);
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; don't fail the test over leftover temp files.
        }
    }
}
