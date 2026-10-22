using System.Runtime.InteropServices;

namespace Goto.Ui;

internal static class Vt
{
    private const int StdErrorHandle = -12;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    /// <summary>
    /// Enables VT escape processing on stderr for the scope's lifetime, restoring the original
    /// console mode on Dispose() even if the scope body throws. A no-op (never throws) if stderr
    /// isn't a real console handle (e.g. redirected).
    /// </summary>
    public sealed class Scope : IDisposable
    {
        private readonly nint _handle;
        private readonly uint _originalMode;
        private readonly bool _changed;

        public Scope()
        {
            _handle = GetStdHandle(StdErrorHandle);
            if (_handle != IntPtr.Zero && _handle != new IntPtr(-1) && GetConsoleMode(_handle, out _originalMode))
            {
                _changed = SetConsoleMode(_handle, _originalMode | EnableVirtualTerminalProcessing);
            }
        }

        public void Dispose()
        {
            if (_changed)
                SetConsoleMode(_handle, _originalMode);
        }
    }
}
