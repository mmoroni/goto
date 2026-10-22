namespace Goto.Cli;

internal sealed class ParsedArgs
{
    public List<string> Positionals { get; } = new();
    public string? InitShell { get; set; }
    public bool Install { get; set; }
}

internal sealed class ArgParseException : Exception
{
    public ArgParseException(string message) : base(message) { }
}

internal static class ArgParser
{
    public static ParsedArgs Parse(string[] args)
    {
        var result = new ParsedArgs();
        bool endOfOptions = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];

            if (!endOfOptions && a == "--")
            {
                endOfOptions = true;
                continue;
            }

            if (!endOfOptions && a == "--init")
            {
                if (i + 1 >= args.Length)
                    throw new ArgParseException("gt: --init requires a shell name, e.g. 'gt --init powershell'");
                result.InitShell = args[++i];
                continue;
            }

            if (!endOfOptions && a == "--install")
            {
                result.Install = true;
                continue;
            }

            if (!endOfOptions && a.Length > 1 && a[0] == '-')
                throw new ArgParseException($"gt: unknown option '{a}'");

            result.Positionals.Add(a);
        }

        return result;
    }
}
