using System.Text;

namespace Goto.Store;

internal static class AtomicFile
{
    public static void WithLock(Action action)
    {
        using var mutex = new Mutex(initiallyOwned: false, @"Local\goto-store");
        bool owned = false;
        try
        {
            owned = mutex.WaitOne(TimeSpan.FromSeconds(5));
            action();
        }
        finally
        {
            if (owned)
                mutex.ReleaseMutex();
        }
    }

    public static void WriteAllLinesAtomic(string path, IEnumerable<string> lines)
    {
        string tmp = path + ".tmp";
        File.WriteAllLines(tmp, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, path, overwrite: true);
    }
}
