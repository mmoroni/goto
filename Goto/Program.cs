using Goto.Cli;
using Goto.Core;
using Goto.Search;
using Goto.Shell;
using Goto.Store;
using Goto.Ui;

// Defensive: avoid legacy console-codepage mangling of stderr messages
// containing non-ASCII paths. Does NOT affect the raw stdout path writer,
// which bypasses Console entirely (see RawOutput).
try
{
    Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
catch (IOException)
{
    // Not attached to a real console (e.g. redirected); ignore.
}

return Run(args);

static int Run(string[] args)
{
    ParsedArgs parsed;
    try
    {
        parsed = ArgParser.Parse(args);
    }
    catch (ArgParseException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return ExitCode.UsageError;
    }

    if (parsed.InitShell is not null)
        return RunInit(parsed.InitShell);

    if (parsed.Install)
        return RunInstall();

    if (parsed.Doctor)
        return RunDoctor();

    if (parsed.Positionals.Count == 0)
    {
        Console.Error.WriteLine("usage: gt <term> [term...] | gt --init powershell | gt --install");
        return ExitCode.UsageError;
    }

    var result = Resolver.Resolve(parsed);

    switch (result.Kind)
    {
        case ResolveKind.Success:
            RawOutput.WriteStdout(result.Path!);
            return ExitCode.Success;

        case ResolveKind.NoMatch:
            Console.Error.WriteLine($"gt: no match for '{string.Join(" ", parsed.Positionals)}'");
            return ExitCode.NoMatch;

        case ResolveKind.TooMany:
            Console.Error.WriteLine($"gt: too many matches ({result.Candidates!.Count}). Be more specific:");
            foreach (string candidate in result.Candidates)
                Console.Error.WriteLine($"  {candidate}");

            IReadOnlyList<string> suggestions = Suggestions.Suggest(result.Candidates, result.LeafTerm!);
            if (suggestions.Count > 0)
            {
                Console.Error.WriteLine("  try:");
                foreach (string s in suggestions)
                    Console.Error.WriteLine($"    {s}");
            }
            return ExitCode.AmbiguousOrCancelled;

        case ResolveKind.BackendError:
            Console.Error.WriteLine($"gt: {result.Path}");
            return ExitCode.BackendOrConfigError;

        case ResolveKind.Listed:
            Console.Error.WriteLine($"gt: {result.Candidates!.Count} match(es):");
            foreach (string candidate in result.Candidates)
                Console.Error.WriteLine($"  {candidate}");
            return ExitCode.Success;

        case ResolveKind.NeedsDecision:
            return DispatchNeedsDecision(parsed, result);

        default:
            Console.Error.WriteLine("gt: internal error");
            return ExitCode.BackendOrConfigError;
    }
}

static int DispatchNeedsDecision(ParsedArgs parsed, ResolveResult result)
{
    bool nonInteractive = Console.IsInputRedirected || parsed.NoUi;
    if (nonInteractive)
    {
        Console.Error.WriteLine($"gt: {result.Candidates!.Count} possible matches, pick one (non-interactive):");
        foreach (string candidate in result.Candidates)
            Console.Error.WriteLine($"  {candidate}");
        return ExitCode.AmbiguousOrCancelled;
    }

    string? chosen = Picker.Run(result.Candidates!);
    if (chosen is null)
        return ExitCode.AmbiguousOrCancelled; // Esc/Ctrl+C: silent, no stdout, no recording

    Resolver.RecordChoice(result.Key!, chosen);
    RawOutput.WriteStdout(chosen);
    return ExitCode.Success;
}

static int RunDoctor()
{
    Config config = Config.Load();

    Console.WriteLine("gt doctor:");

    // A single TryConnect call drives both lines below -- calling it twice (once directly,
    // once again inside BackendSelector.Select) risks launching Everything.exe a second time
    // if its DB takes longer than ~2s to load (the first call's auto-start attempt may still
    // be mid-flight when a second, independent attempt starts polling from zero).
    var everything = new EverythingSearcher(config);
    bool everythingOk = everything.TryConnect(out string? whyNot);
    Console.WriteLine(everythingOk
        ? "  Everything: reachable"
        : $"  Everything: not reachable ({whyNot})");

    string activeBackend = config.Backend switch
    {
        BackendMode.Walk => "walk",
        BackendMode.Everything => everythingOk ? "everything" : "none (error: configured but unreachable)",
        _ => everythingOk ? "everything" : "walk", // Auto
    };
    Console.WriteLine($"  active backend: {activeBackend} (configured: {config.Backend.ToString().ToLowerInvariant()})");

    string dataDir = DataPaths.ResolveDataDir();
    bool writable = TryProbeWrite(dataDir, out string? writeError);
    Console.WriteLine(writable
        ? $"  data dir: {dataDir} (writable)"
        : $"  data dir: {dataDir} (NOT writable: {writeError})");

    return ExitCode.Success;
}

static bool TryProbeWrite(string dir, out string? error)
{
    string probe = Path.Combine(dir, $".doctor-probe-{Guid.NewGuid():N}.tmp");
    try
    {
        File.WriteAllText(probe, "probe");
        File.Delete(probe);
        error = null;
        return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        error = ex.Message;
        return false;
    }
}

static int RunInit(string shell)
{
    if (!string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"gt: unsupported shell '{shell}' (only 'powershell' is supported in this build)");
        return ExitCode.UsageError;
    }

    RawOutput.WriteStdout(PowerShellInit.Script);
    return ExitCode.Success;
}

static int RunInstall()
{
    string exePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("could not determine own executable path");

    string profilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "WindowsPowerShell",
        "Microsoft.PowerShell_profile.ps1");

    const string beginMarker = "# >>> goto >>>";
    const string endMarker = "# <<< goto <<<";

    string block =
        $"{beginMarker}\n" +
        $"(& '{exePath}' --init powershell) | Invoke-Expression\n" +
        $"{endMarker}\n";

    try
    {
        string? dir = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string existing = File.Exists(profilePath) ? File.ReadAllText(profilePath) : string.Empty;

        if (existing.Contains(beginMarker, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"gt: already installed in {profilePath}");
        }
        else
        {
            string sep = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "";
            File.AppendAllText(profilePath, sep + block);
            Console.Error.WriteLine($"gt: installed into {profilePath}");
        }

        Console.Error.WriteLine(
            "gt: if PowerShell refuses to run the profile, run once: " +
            "Set-ExecutionPolicy -Scope CurrentUser RemoteSigned");

        return ExitCode.Success;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"gt: failed to install: {ex.Message}");
        return ExitCode.BackendOrConfigError;
    }
}
