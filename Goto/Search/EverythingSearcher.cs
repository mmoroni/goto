using System.Diagnostics;
using Goto.Store;

namespace Goto.Search;

internal sealed class EverythingSearcher(Config config) : IFolderSearcher
{
    private const int PollAttempts = 20;
    private const int PollIntervalMs = 100; // 20 x 100ms = ~2s, per spec's "poll about 2 seconds"

    public string Name => "everything";

    public bool TryConnect(out string? whyNot)
    {
        string dllPath = NativeLibraryExtractor.EnsureExtracted();
        EverythingNative.EnsureDllImportResolverRegistered(dllPath);

        if (IsIpcUp())
        {
            whyNot = null;
            return true;
        }

        if (!TryLocateEverythingExe(out string? exePath))
        {
            whyNot = "Everything.exe not found (checked config, running processes, Program Files)";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(exePath!, "-startup")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            whyNot = $"failed to start Everything.exe: {ex.Message}";
            return false;
        }

        for (int i = 0; i < PollAttempts; i++)
        {
            Thread.Sleep(PollIntervalMs);
            if (IsIpcUp())
            {
                whyNot = null;
                return true;
            }
        }

        whyNot = "Everything did not become ready within 2s of starting it";
        return false;
    }

    private static bool IsIpcUp()
    {
        // Cheapest possible "is it already up" check per the interface's own "cheap check,
        // no heavy work" contract: a near-free call, then inspect the resulting error state,
        // rather than issuing a real SetSearchW/QueryW round trip.
        bool dbLoaded = EverythingNative.Everything_IsDBLoaded();
        uint err = EverythingNative.Everything_GetLastError();
        return err != EverythingNative.EVERYTHING_ERROR_IPC && dbLoaded;
    }

    private bool TryLocateEverythingExe(out string? exePath)
    {
        if (!string.IsNullOrWhiteSpace(config.EverythingPath) && File.Exists(config.EverythingPath))
        {
            exePath = config.EverythingPath;
            return true;
        }

        foreach (Process proc in Process.GetProcessesByName("Everything"))
        {
            try
            {
                string? path = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                {
                    exePath = path;
                    return true;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Access denied reading MainModule (e.g. different user/elevation) -- skip.
            }
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(programFiles, "Everything*"))
            {
                string candidate = Path.Combine(dir, "Everything.exe");
                if (File.Exists(candidate))
                {
                    exePath = candidate;
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Fall through to "not found".
        }

        exePath = null;
        return false;
    }

    public IReadOnlyList<string> FindFolders(
        string leaf, IReadOnlyList<string> qualifiers,
        MatchMode mode, int limit, CancellationToken ct)
    {
        string query = EverythingQueryBuilder.Build(leaf, qualifiers, mode);

        EverythingNative.Everything_SetSearchW(query);
        EverythingNative.Everything_SetRequestFlags(EverythingNative.EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME);
        EverythingNative.Everything_SetMax((uint)limit);
        EverythingNative.Everything_QueryW(bWait: true);

        uint count = EverythingNative.Everything_GetNumResults();
        var results = new List<string>((int)count);

        unsafe
        {
            const int bufLen = 32768; // generous, long-path-safe
            char* buf = stackalloc char[bufLen];
            for (uint i = 0; i < count; i++)
            {
                if (ct.IsCancellationRequested)
                    break;

                uint written = EverythingNative.Everything_GetResultFullPathNameW(i, buf, bufLen);
                if (written > 0)
                    results.Add(new string(buf, 0, (int)written));
            }
        }

        return results;
    }
}
