using System.Runtime.InteropServices;

namespace Goto.Search;

internal static class EverythingNative
{
    internal const string DllName = "Everything64.dll";

    // Verified against Everything SDK v2's Everything.h (extracted from
    // https://www.voidtools.com/Everything-SDK.zip).
    internal const uint EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME = 0x00000004;
    internal const uint EVERYTHING_OK = 0;
    internal const uint EVERYTHING_ERROR_IPC = 2;

    [DllImport(DllName, CharSet = CharSet.Unicode)]
    internal static extern void Everything_SetSearchW(string lpSearchString);

    [DllImport(DllName)]
    internal static extern void Everything_SetRequestFlags(uint dwRequestFlags);

    [DllImport(DllName)]
    internal static extern void Everything_SetMax(uint dwMax);

    [DllImport(DllName)]
    internal static extern bool Everything_QueryW(bool bWait);

    [DllImport(DllName)]
    internal static extern uint Everything_GetNumResults();

    [DllImport(DllName, EntryPoint = "Everything_GetResultFullPathNameW")]
    internal static extern unsafe uint Everything_GetResultFullPathNameW(uint nIndex, char* lpString, uint nMaxCount);

    [DllImport(DllName)]
    internal static extern uint Everything_GetLastError();

    [DllImport(DllName)]
    internal static extern bool Everything_IsDBLoaded();

    private static bool _resolverRegistered;

    /// <summary>
    /// Must be called once, before any Everything_* P/Invoke, with the full path of the
    /// extracted DLL. Redirects the bare "Everything64.dll" DllImport name to the real
    /// on-disk path under the data dir's native\ subfolder.
    /// </summary>
    internal static void EnsureDllImportResolverRegistered(string extractedDllFullPath)
    {
        if (_resolverRegistered)
            return;

        NativeLibrary.SetDllImportResolver(typeof(EverythingNative).Assembly, (libraryName, _, _) =>
            libraryName == DllName ? NativeLibrary.Load(extractedDllFullPath) : IntPtr.Zero);

        _resolverRegistered = true;
    }
}
