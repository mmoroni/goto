namespace Goto.Core;

internal static class PathShortcut
{
    public static bool TryResolve(string term, out string resolvedPath)
    {
        resolvedPath = string.Empty;

        string expanded = ExpandTilde(term);

        string full;
        try
        {
            full = Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!Directory.Exists(full))
            return false;

        resolvedPath = NormalizeTrailingSeparator(full);
        return true;
    }

    private static string ExpandTilde(string term)
    {
        if (term == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (term.Length > 1 && term[0] == '~' && (term[1] == '/' || term[1] == '\\'))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, term[2..]);
        }

        return term;
    }

    private static string NormalizeTrailingSeparator(string full)
    {
        bool isDriveRoot = full.Length == 3 && full[1] == ':' && full[2] == '\\';
        return isDriveRoot ? full : full.TrimEnd('\\', '/');
    }
}
