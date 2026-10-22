using System.Globalization;
using Goto.Store;

namespace Goto.Search;

/// <summary>
/// Prints the "falling back to the walker" hint at most once per rate-limit window, tracked
/// via a plain timestamp file (no mutex -- worst case on a race is printing the hint twice,
/// which is harmless, unlike decision/visit data).
/// </summary>
internal static class EverythingFallbackHint
{
    private const string FileName = "last-everything-hint.txt";
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(30);

    public static void MaybePrint(string? whyNot)
    {
        string path = Path.Combine(DataPaths.ResolveDataDir(), FileName);
        DateTime now = DateTime.UtcNow;

        if (File.Exists(path) &&
            DateTime.TryParse(
                File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime last) &&
            now - last < MinInterval)
        {
            return;
        }

        Console.Error.WriteLine($"gt: Everything unavailable ({whyNot}); using filesystem walker");

        try
        {
            File.WriteAllText(path, now.ToString("O"));
        }
        catch (IOException)
        {
            // Best-effort; never block resolution over a hint-timestamp write failure.
        }
    }
}
