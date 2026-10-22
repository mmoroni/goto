namespace Goto.Shell;

internal static class RawOutput
{
    public static void WriteStdout(string text)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        using System.IO.Stream stdout = System.Console.OpenStandardOutput();
        stdout.Write(bytes, 0, bytes.Length);
        stdout.Flush();
    }
}
