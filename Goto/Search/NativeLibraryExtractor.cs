using System.Security.Cryptography;
using Goto.Store;

namespace Goto.Search;

internal static class NativeLibraryExtractor
{
    /// <summary>
    /// Lazily extracts the embedded Everything64.dll to the data dir's native\ subfolder,
    /// re-extracting only if the embedded bytes' hash differs from the sidecar hash file.
    /// Returns the full path to the extracted DLL.
    /// </summary>
    public static string EnsureExtracted()
    {
        string nativeDir = Path.Combine(DataPaths.ResolveDataDir(), "native");
        Directory.CreateDirectory(nativeDir);

        string dllPath = Path.Combine(nativeDir, "Everything64.dll");
        string hashPath = dllPath + ".sha256";

        byte[] embeddedBytes = ReadEmbeddedBytes();
        string embeddedHash = Convert.ToHexString(SHA256.HashData(embeddedBytes));

        bool needsExtract = true;
        if (File.Exists(dllPath) && File.Exists(hashPath))
        {
            string existingHash = File.ReadAllText(hashPath).Trim();
            needsExtract = !string.Equals(existingHash, embeddedHash, StringComparison.OrdinalIgnoreCase);
        }

        if (needsExtract)
        {
            string tmpDll = dllPath + ".tmp";
            File.WriteAllBytes(tmpDll, embeddedBytes);
            File.Move(tmpDll, dllPath, overwrite: true);
            File.WriteAllText(hashPath, embeddedHash);
        }

        return dllPath;
    }

    private static byte[] ReadEmbeddedBytes()
    {
        var asm = typeof(NativeLibraryExtractor).Assembly;
        using Stream? s = asm.GetManifestResourceStream("Everything64.dll");
        if (s is null)
            throw new InvalidOperationException("embedded Everything64.dll resource not found");

        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
