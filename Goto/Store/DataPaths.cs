namespace Goto.Store;

internal static class DataPaths
{
    public static string DecisionsFile => Path.Combine(ResolveDataDir(), "decisions.tsv");
    public static string VisitsFile => Path.Combine(ResolveDataDir(), "visits.tsv");
    public static string ConfigFile => Path.Combine(ResolveDataDir(), "config.txt");

    public static string ResolveDataDir()
    {
        string? gotoHome = Environment.GetEnvironmentVariable("GOTO_HOME");
        if (!string.IsNullOrEmpty(gotoHome))
        {
            Directory.CreateDirectory(gotoHome);
            return gotoHome;
        }

        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(exeDir))
        {
            string portable = Path.Combine(exeDir, "goto-data");
            if (Directory.Exists(portable))
                return portable;
        }

        string localAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "goto");
        Directory.CreateDirectory(localAppData);
        return localAppData;
    }
}
