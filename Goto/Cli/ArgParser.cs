namespace Goto.Cli;

internal sealed class ParsedArgs
{
    public List<string> Positionals { get; } = new();
    public string? InitShell { get; set; }
    public bool Install { get; set; }
    public bool List { get; set; }
    public bool Pick { get; set; }
    public bool NoUi { get; set; }
    public bool Doctor { get; set; }
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

            if (!endOfOptions && (a == "-l" || a == "--list"))
            {
                result.List = true;
                continue;
            }

            if (!endOfOptions && (a == "-p" || a == "--pick"))
            {
                result.Pick = true;
                continue;
            }

            if (!endOfOptions && a == "--no-ui")
            {
                result.NoUi = true;
                continue;
            }

            if (!endOfOptions && a == "--doctor")
            {
                result.Doctor = true;
                continue;
            }

            if (!endOfOptions && a.Length > 1 && a[0] == '-')
                throw new ArgParseException($"gt: unknown option '{a}'");

            result.Positionals.Add(a);
        }

        if (result.List && result.Pick)
            throw new ArgParseException("gt: --list and --pick are mutually exclusive");

        return result;
    }
}
