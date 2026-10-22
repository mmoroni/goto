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

    if (parsed.Help)
        return RunHelp();

    if (parsed.InitShell is not null)
        return RunInit(parsed.InitShell, parsed.WithPromptHook);

    if (parsed.Install)
        return RunInstall(parsed.WithPromptHook);

    if (parsed.Doctor)
        return RunDoctor();

    if (parsed.AddPath is not null)
        return RunAdd(parsed.AddPath);

    if (parsed.ForgetTerms is not null)
        return RunForget(parsed.ForgetTerms);

    if (parsed.AliasName is not null)
        return RunAlias(parsed.AliasName, parsed.AliasPath!);

    if (parsed.UnaliasName is not null)
        return RunUnalias(parsed.UnaliasName);

    if (parsed.ListAliases)
        return RunAliasesList();

    if (parsed.CompleteWord is not null)
        return RunComplete(parsed.CompleteWord);

    if (parsed.Wrong)
        return RunWrong(parsed.Explore, parsed.Code);

    if (parsed.History)
        return RunHistory(parsed.Explore, parsed.Code);

    if (parsed.Positionals.Count == 0)
    {
        Console.Error.WriteLine(HelpText.ShortUsage);
        return ExitCode.UsageError;
    }

    var result = Resolver.Resolve(parsed);

    if (parsed.Explain && result.Explain is not null)
        PrintExplain(result.Explain);

    return DispatchResolveResult(parsed, result);
}

static int DispatchResolveResult(ParsedArgs parsed, ResolveResult result)
{
    switch (result.Kind)
    {
        case ResolveKind.Success:
            return DispatchSuccessOrOpen(parsed.Explore, parsed.Code, result.Path!);

        case ResolveKind.NoMatch:
            Console.Error.WriteLine($"gt: no match for '{string.Join(" ", parsed.Positionals)}'");
            if (result.DidYouMean is { Count: > 0 } didYouMean)
            {
                Console.Error.WriteLine(didYouMean.Count == 1
                    ? $"gt: did you mean '{didYouMean[0]}'?"
                    : $"gt: did you mean one of: {string.Join(", ", didYouMean)}?");
            }
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

static int DispatchSuccessOrOpen(bool explore, bool code, string path)
{
    if (explore || code)
    {
        OpenTarget target = explore ? OpenTarget.Explorer : OpenTarget.Code;
        if (!ExternalOpener.TryLaunch(target, path, out string? error))
        {
            string toolName = explore ? "Explorer" : "VS Code";
            string hint = explore ? "" : " (is 'code' on PATH?)";
            Console.Error.WriteLine($"gt: could not open in {toolName}{hint}: {error}");
            return ExitCode.BackendOrConfigError;
        }
        // Nothing on stdout -- the PowerShell wrapper's `if ($code -eq 0 -and $out)` guard
        // naturally skips Set-Location, since opening in Explorer/Code should never also cd.
        return ExitCode.Success;
    }

    RawOutput.WriteStdout(path);
    return ExitCode.Success;
}

static void PrintExplain(ExplainTrace trace)
{
    string sourceDesc = trace.Source switch
    {
        ExplainSource.PathShortcut => "path shortcut",
        ExplainSource.Alias => "alias",
        ExplainSource.Decision => "decision",
        ExplainSource.Tier => $"tier ({trace.Tier.ToString()!.ToLowerInvariant()})",
        _ => "unknown",
    };
    Console.Error.WriteLine($"gt: source: {sourceDesc}");

    if (trace.Breakdown.Count == 0)
        return;

    Console.Error.WriteLine("gt: candidates (ranked):");
    for (int i = 0; i < trace.Breakdown.Count; i++)
    {
        ScoreBreakdown b = trace.Breakdown[i];
        Console.Error.WriteLine($"  {i + 1}. {b.Path}  (score {b.Total:F2})");
        Console.Error.WriteLine(
            $"       frecency {b.FrecencyTerm,7:+0.00;-0.00;+0.00}" +
            $"  cwd {b.CwdBonus,6:+0.00;-0.00;+0.00}" +
            $"  parent {b.ParentBonus,6:+0.00;-0.00;+0.00}" +
            $"  profile {b.ProfileBonus,6:+0.00;-0.00;+0.00}" +
            $"  project-root {b.ProjectRootBonus,6:+0.00;-0.00;+0.00}" +
            $"  depth {b.DepthPenalty,7:+0.00;-0.00;+0.00}");
    }
}

static int RunAdd(string path)
{
    try
    {
        VisitStore.Increment(path);
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
    {
        // Malformed input from a scripted caller (the prompt hook) must never surface an
        // error on every prompt redraw -- matches VisitStore's existing lenient philosophy.
    }

    return ExitCode.Success; // prints nothing, always exit 0
}

static int RunForget(IReadOnlyList<string> terms)
{
    GotoQuery query = GotoQuery.FromPositionals(terms);
    bool existed = DecisionStore.TryGet(query.Key, out _);
    DecisionStore.Remove(query.Key);

    Console.Error.WriteLine(existed
        ? $"gt: forgot '{string.Join(" ", terms)}'"
        : $"gt: no remembered decision for '{string.Join(" ", terms)}'");

    return ExitCode.Success; // both outcomes exit 0 -- "already forgotten" isn't an error
}

static int RunAlias(string name, string path)
{
    string full;
    try
    {
        full = Path.GetFullPath(path);
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
    {
        Console.Error.WriteLine($"gt: invalid path '{path}': {ex.Message}");
        return ExitCode.UsageError;
    }

    if (!Directory.Exists(full))
        Console.Error.WriteLine($"gt: warning: '{full}' does not exist yet (alias saved anyway)");

    AliasStore.Upsert(name, full);
    Console.Error.WriteLine($"gt: aliased '{name}' -> {full}");
    return ExitCode.Success;
}

static int RunUnalias(string name)
{
    bool existed = AliasStore.TryGet(name, out _);
    AliasStore.Remove(name);
    Console.Error.WriteLine(existed ? $"gt: removed alias '{name}'" : $"gt: no such alias '{name}'");
    return ExitCode.Success;
}

static int RunAliasesList()
{
    var aliases = AliasStore.LoadAll();
    if (aliases.Count == 0)
    {
        Console.WriteLine("gt: no aliases defined");
        return ExitCode.Success;
    }

    Console.WriteLine("gt: aliases:");
    foreach (var (name, path) in aliases.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        Console.WriteLine($"  {name} -> {path}");

    return ExitCode.Success;
}

static int RunComplete(string word)
{
    var visits = VisitStore.LoadAll();

    var candidates = visits
        .GroupBy(kv => Path.GetFileName(kv.Key), StringComparer.OrdinalIgnoreCase)
        .Where(g => !string.IsNullOrEmpty(g.Key))
        .Select(g => (Name: g.Key, Best: g.OrderByDescending(kv => kv.Value.VisitCount).First().Value))
        .Where(x => x.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(x => x.Best.VisitCount)
        .ThenByDescending(x => x.Best.LastVisitUtc)
        .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        .Select(x => x.Name);

    foreach (string name in candidates)
        Console.WriteLine(name);

    return ExitCode.Success;
}

static int RunWrong(bool explore, bool code)
{
    if (!LastResolutionStore.TryGet(out string key, out string lastPath))
    {
        Console.Error.WriteLine("gt: nothing to correct (no previous resolution remembered)");
        return ExitCode.NoMatch;
    }

    string[] terms = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (terms.Length == 0)
    {
        Console.Error.WriteLine("gt: nothing to correct (no previous resolution remembered)");
        return ExitCode.NoMatch;
    }

    var synthetic = ArgParser.Parse(["--pick", .. terms]);
    synthetic.Explore = explore;
    synthetic.Code = code;
    var result = Resolver.Resolve(synthetic, excludePath: lastPath);

    if (result.Kind == ResolveKind.NoMatch)
    {
        Console.Error.WriteLine($"gt: no other candidate for '{string.Join(" ", terms)}' besides '{lastPath}'");
        return ExitCode.NoMatch;
    }

    return DispatchResolveResult(synthetic, result);
}

static int RunHistory(bool explore, bool code)
{
    var visits = VisitStore.LoadAll();
    if (visits.Count == 0)
    {
        Console.Error.WriteLine("gt: no visit history yet");
        return ExitCode.NoMatch;
    }

    IReadOnlyList<string> ordered = HistoryOrdering.OrderByRecency(visits);

    if (Console.IsInputRedirected)
    {
        Console.Error.WriteLine($"gt: {ordered.Count} visited folder(s) (non-interactive):");
        foreach (string p in ordered)
            Console.Error.WriteLine($"  {p}");
        return ExitCode.AmbiguousOrCancelled;
    }

    string? chosen = Picker.Run(ordered);
    if (chosen is null)
        return ExitCode.AmbiguousOrCancelled; // Esc/Ctrl+C: silent, matches the resolve-picker convention

    // Deliberately NOT DecisionStore/Resolver.RecordChoice (browsing history has no query key to
    // attach a decision to) and NOT VisitStore.Increment (if the prompt hook is enabled, the
    // resulting Set-Location is already picked up on the next prompt redraw -- incrementing here
    // too would double-count; without the hook this behaves like a plain `cd`, which already has
    // no frecency effect).
    return DispatchSuccessOrOpen(explore, code, chosen);
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
    return DispatchSuccessOrOpen(parsed.Explore, parsed.Code, chosen);
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

static int RunHelp()
{
    // Explicit, successful request -- stdout, exit 0 (unlike the terse ShortUsage one-liner
    // printed to stderr on a bare usage error). Human-facing, not consumed by any script, so
    // Console.WriteLine is fine here -- no need for the RawOutput chokepoint.
    Console.WriteLine(HelpText.Full);
    return ExitCode.Success;
}

static int RunInit(string shell, bool withPromptHook)
{
    if (!string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"gt: unsupported shell '{shell}' (only 'powershell' is supported in this build)");
        return ExitCode.UsageError;
    }

    string script = PowerShellInit.Script + PowerShellInit.TabCompletionBlock + PowerShellInit.HistoryHotkeyBlock;
    if (withPromptHook)
        script += PowerShellInit.PromptHookAddendum;

    RawOutput.WriteStdout(script);
    return ExitCode.Success;
}

static int RunInstall(bool withPromptHook)
{
    string exePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("could not determine own executable path");

    string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    // Both profile targets are valid regardless of which shell happens to run --install, and
    // --init powershell's script content doesn't vary by PowerShell edition -- so install into
    // both unconditionally rather than gating behind a new flag. $PROFILE's path formula (folder
    // name differs by edition: "WindowsPowerShell" for Desktop, "PowerShell" for Core) is a
    // stable, documented PowerShell convention -- hardcoded here rather than shelling out to
    // `pwsh` to confirm, since that would fail outright on a machine without pwsh installed.
    string ps51Profile = Path.Combine(documents, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
    string pwsh7Profile = Path.Combine(documents, "PowerShell", "Microsoft.PowerShell_profile.ps1");

    bool ok1 = TryInstallProfile(ps51Profile, exePath, withPromptHook, "Windows PowerShell 5.1");
    bool ok2 = TryInstallProfile(pwsh7Profile, exePath, withPromptHook, "PowerShell 7 (pwsh)");

    Console.Error.WriteLine(
        "gt: if PowerShell refuses to run the profile, run once: " +
        "Set-ExecutionPolicy -Scope CurrentUser RemoteSigned");

    // Best-effort, not transactional: one profile can fail while the other still installs.
    return (ok1 && ok2) ? ExitCode.Success : ExitCode.BackendOrConfigError;
}

static bool TryInstallProfile(string profilePath, string exePath, bool withPromptHook, string label)
{
    string block = ProfileInstallBlock.Build(exePath, withPromptHook);

    try
    {
        string? dir = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string existing = File.Exists(profilePath) ? File.ReadAllText(profilePath) : string.Empty;

        if (existing.Contains(ProfileInstallBlock.BeginMarker, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"gt: already installed for {label} in {profilePath}");
        }
        else
        {
            string sep = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "";
            File.AppendAllText(profilePath, sep + block);
            Console.Error.WriteLine($"gt: installed for {label} into {profilePath}");
        }

        return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"gt: failed to install for {label} ({profilePath}): {ex.Message}");
        return false;
    }
}
