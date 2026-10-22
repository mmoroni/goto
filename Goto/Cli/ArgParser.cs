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
    public bool Explain { get; set; }
    public string? AddPath { get; set; }
    public bool WithPromptHook { get; set; }
    public List<string>? ForgetTerms { get; set; }
    public string? AliasName { get; set; }
    public string? AliasPath { get; set; }
    public string? UnaliasName { get; set; }
    public bool ListAliases { get; set; }
    public string? CompleteWord { get; set; }
    public bool Wrong { get; set; }
    public bool Explore { get; set; }
    public bool Code { get; set; }
    public bool History { get; set; }
    public bool Help { get; set; }
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

            if (!endOfOptions && (a == "-h" || a == "--help"))
            {
                result.Help = true;
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

            if (!endOfOptions && a == "--explain")
            {
                result.Explain = true;
                continue;
            }

            if (!endOfOptions && a == "--with-prompt-hook")
            {
                result.WithPromptHook = true;
                continue;
            }

            if (!endOfOptions && a == "--add")
            {
                if (i + 1 >= args.Length)
                    throw new ArgParseException("gt: --add requires a path, e.g. 'gt --add C:\\some\\folder'");
                result.AddPath = args[++i];
                continue;
            }

            if (!endOfOptions && a == "--forget")
            {
                var terms = new List<string>();
                bool forgetEndOfOptions = false;
                for (int j = i + 1; j < args.Length; j++)
                {
                    if (!forgetEndOfOptions && args[j] == "--")
                    {
                        forgetEndOfOptions = true;
                        continue;
                    }
                    terms.Add(args[j]);
                }

                if (terms.Count == 0)
                    throw new ArgParseException("gt: --forget requires at least one term, e.g. 'gt --forget documents'");

                result.ForgetTerms = terms;
                break; // --forget consumes the rest of argv; nothing else to parse
            }

            if (!endOfOptions && a == "--alias")
            {
                if (i + 2 >= args.Length)
                    throw new ArgParseException("gt: --alias requires a name and a path, e.g. 'gt --alias work D:\\Clients\\Acme'");
                result.AliasName = args[++i];
                result.AliasPath = args[++i];
                continue;
            }

            if (!endOfOptions && a == "--unalias")
            {
                if (i + 1 >= args.Length)
                    throw new ArgParseException("gt: --unalias requires a name, e.g. 'gt --unalias work'");
                result.UnaliasName = args[++i];
                continue;
            }

            if (!endOfOptions && a == "--aliases")
            {
                result.ListAliases = true;
                continue;
            }

            // Single-token "--complete=WORD" form (WORD may be empty, e.g. "--complete=").
            // PowerShell silently drops a literal empty-string ARGUMENT when invoking a native
            // exe (confirmed empirically -- not just a trailing-argument quirk, any position),
            // so the real completer script uses this form specifically to avoid ever passing an
            // empty string as its own argv element: the whole token is never empty, even when
            // the completion word itself is.
            if (!endOfOptions && a.StartsWith("--complete="))
            {
                result.CompleteWord = a["--complete=".Length..];
                continue;
            }

            if (!endOfOptions && a == "--complete")
            {
                if (i + 1 >= args.Length)
                    throw new ArgParseException("gt: --complete requires a word argument (may be empty), e.g. 'gt --complete do'");
                result.CompleteWord = args[++i];
                continue;
            }

            if (!endOfOptions && a == "--wrong")
            {
                result.Wrong = true;
                continue;
            }

            if (!endOfOptions && (a == "-e" || a == "--explore"))
            {
                result.Explore = true;
                continue;
            }

            if (!endOfOptions && (a == "-c" || a == "--code"))
            {
                result.Code = true;
                continue;
            }

            if (!endOfOptions && a == "--history")
            {
                result.History = true;
                continue;
            }

            if (!endOfOptions && a.Length > 1 && a[0] == '-')
                throw new ArgParseException($"gt: unknown option '{a}'");

            result.Positionals.Add(a);
        }

        if (result.List && result.Pick)
            throw new ArgParseException("gt: --list and --pick are mutually exclusive");

        if (result.Explore && result.Code)
            throw new ArgParseException("gt: --explore and --code are mutually exclusive");

        if (result.List && (result.Explore || result.Code))
            throw new ArgParseException("gt: --list cannot be combined with --explore/--code");

        return result;
    }
}
