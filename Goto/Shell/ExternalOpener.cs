using System.Diagnostics;

namespace Goto.Shell;

internal enum OpenTarget { Explorer, Code }

internal static class ExternalOpener
{
    /// <summary>Pure: the ProcessStartInfo that would be used. No side effects -- unit-testable.</summary>
    public static ProcessStartInfo BuildStartInfo(OpenTarget target, string path)
    {
        var psi = new ProcessStartInfo();

        switch (target)
        {
            case OpenTarget.Explorer:
                // explorer.exe is always resolvable via raw CreateProcess's own search path on a
                // standard Windows install -- no shell resolution needed.
                psi.FileName = "explorer.exe";
                psi.UseShellExecute = false;
                break;

            case OpenTarget.Code:
                // "code" is normally a .cmd shim on PATH, not a bare .exe. Raw CreateProcess
                // (UseShellExecute = false) does not apply PATHEXT resolution to find it;
                // ShellExecute resolves bare commands via PATH + PATHEXT + App Paths the same
                // way a user's own shell / Win+R would, so UseShellExecute = true is required.
                psi.FileName = "code";
                psi.UseShellExecute = true;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }

        psi.ArgumentList.Add(path);
        return psi;
    }

    /// <summary>Side-effecting: actually launches. Not unit-tested (would pop a real window).</summary>
    public static bool TryLaunch(OpenTarget target, string path, out string? error)
    {
        error = null;
        try
        {
            using Process? proc = Process.Start(BuildStartInfo(target, path));
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            error = ex.Message;
            return false;
        }
    }
}
