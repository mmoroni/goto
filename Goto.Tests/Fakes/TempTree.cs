namespace Goto.Tests.Fakes;

/// <summary>A throwaway directory tree for a single test, deleted on disposal.</summary>
internal sealed class TempTree : IDisposable
{
    public string Root { get; }

    public TempTree()
    {
        Root = Directory.CreateTempSubdirectory("goto-test-tree-").FullName;
    }

    public string CreateDir(string relativePath)
    {
        string full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; don't fail the test over leftover temp files.
        }
    }
}
