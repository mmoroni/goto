namespace Goto.Store;

internal static class PathCanon
{
    public static string Canonicalize(string path)
    {
        string full = Path.GetFullPath(path);
        bool isDriveRoot = full.Length == 3 && full[1] == ':' && full[2] == '\\';
        return isDriveRoot ? full : full.TrimEnd('\\', '/');
    }
}
