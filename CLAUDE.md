# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`goto` (alias `gt`) is a Windows directory-jumping CLI: a native AOT C# exe (`goto.exe`) does all
the work and prints the destination path to stdout; a 6-line PowerShell function (emitted by
`goto --init powershell`) calls it and runs `Set-Location`. A child process can't change its
parent shell's working directory, hence the split.

The full design spec (architecture, on-disk file formats, ranking algorithm, Everything-search
integration, picker UI, full build order across 7 milestones) lives in `docs/specs/00-design-spec.md`,
with one `docs/specs/milestone-0N-*.md` per build-order milestone — see `docs/specs/README.md` for
the index and each milestone's status. **This repo currently implements Milestones 1-2** (skeleton,
and the walker + resolver + decisions store). Do not add the Everything SDK integration, the
interactive picker, ranking/frecency, or alias/completion commands until asked — those belong to
later milestones and would be half-finished implementations right now. When a milestone ships,
update its status and add implementation notes in its spec file, following the pattern in
`milestone-01-skeleton.md`/`milestone-02-walker-resolver-decisions.md`.

## Build

Native AOT publish requires the MSVC linker (`link.exe`), which isn't on `PATH` by default even
with VS Build Tools' C++ workload installed. Use the provided script, which loads the
`VsDevCmd.bat` environment (and additionally puts the VS Installer dir on `PATH`, since the
ILCompiler build targets shell out to `vswhere.exe` directly, which `VsDevCmd.bat` doesn't add):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

This runs `dotnet publish Goto\Goto.csproj -c Release -r win-x64`. Output exe:
`Goto\bin\Release\net10.0\win-x64\publish\goto.exe` (~1.2 MB).

A bare `dotnet publish` from a normal shell will fail with "Platform linker not found" unless
you're already in a Developer Command Prompt / `VsDevCmd.bat`-initialized shell.

This repo has its own `NuGet.Config` (scoped to this project, not the global user config) adding
`nuget.org` as a source — the global user NuGet config on this machine has no sources configured.

## Testing

`Goto.Tests/` is an xUnit project (referenced via `goto.slnx`). Run with:

```powershell
dotnet test Goto.Tests/Goto.Tests.csproj
```

`dotnet test`/`dotnet build` don't invoke the Native AOT linker (that's `dotnet publish`-only), so
this works from a plain shell without `build.ps1`'s MSVC environment. Tests disable collection
parallelization assembly-wide (`Goto.Tests/AssemblyInfo.cs`) because they override the process-wide
`GOTO_HOME` env var per test to isolate the data directory — don't remove that attribute without
threading a data-dir parameter through the stores instead.

**Test fixtures live under `%LOCALAPPDATA%\Temp`** (via `Directory.CreateTempSubdirectory()`), which
the real default `exclude` config entry (`...;AppData\Local\Temp`) would otherwise prune during a
walk. Tests that exercise `WalkSearcher`/`Resolver` against real temp trees must pass a `Config`
with `Exclude = []` (or an explicit non-default list) — see `milestone-02-walker-resolver-decisions.md`
for why this isn't a product bug.

## Golden rule: stdout discipline

**stdout carries ONLY the resolved destination path** during normal term resolution — nothing else,
ever. All human-facing messages (errors, usage, hints) go to stderr. The PowerShell wrapper relies
on this: it captures `$out = & $GOTO_EXE @args` and does `Set-Location -LiteralPath (@($out)[0])`.

The only writer of stdout bytes anywhere in the program is `Shell/RawOutput.WriteStdout`, which
uses `Console.OpenStandardOutput()` directly — never `Console.Out` / `Console.WriteLine`. This
avoids Windows console-codepage mangling of non-ASCII paths. `Console.Out`/`Console.Error` are
fine (and used) for stderr messages.

`--init` and `--install` are meta-commands, not resolutions — `--init powershell`'s script-text
output legitimately goes to stdout too (via the same `RawOutput` chokepoint), since it's consumed
by `| Invoke-Expression`, a different contract entirely. These are mutually exclusive branches in
`Program.cs` from the resolution path, so the two contracts never collide.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success, path on stdout |
| 1 | No match |
| 2 | Too many matches (M2: always this; a real picker/cancel path is Milestone 3) |
| 3 | Backend or config error |
| 4 | Usage error |

## Layout

```
Goto/Program.cs              entry point, exit-code dispatch, --init/--install handlers
Goto/Cli/ArgParser.cs         argv -> ParsedArgs (terms, --, --init, --install)
Goto/Core/ExitCode.cs         exit code constants
Goto/Core/PathShortcut.cs     single-term existing-path check (., .., ~, C:\x, relative dirs)
Goto/Core/GotoQuery.cs        term splitting (/ and \ within an arg) + leaf/qualifiers/decision key
Goto/Core/Matcher.cs          tier leaf-match (T1-T3) + qualifier ancestor-match
Goto/Core/Resolver.cs         full resolution pipeline: path shortcut, decision, tiers, upsert
Goto/Search/IFolderSearcher.cs  backend interface (shared by WalkSearcher now, Everything in M4)
Goto/Search/WalkSearcher.cs   fallback filesystem walker (the only backend until M4)
Goto/Store/DataPaths.cs       GOTO_HOME / portable-mode / %LOCALAPPDATA% resolution
Goto/Store/AtomicFile.cs      named-mutex + tmp-then-rename write primitive
Goto/Store/Config.cs          config.txt parser (exclude / walk_roots / walk_timeout_ms only)
Goto/Store/DecisionStore.cs   decisions.tsv read/write + stale-entry cleanup
Goto/Store/VisitStore.cs      visits.tsv increment-only (no reader -- that's Milestone 5)
Goto/Shell/PowerShellInit.cs  --init powershell script text (verbatim — don't reword/retune)
Goto/Shell/RawOutput.cs       byte-exact stdout writer (the only stdout writer in the program)
```

`goto.slnx` (note: `.slnx`, not `.sln` — .NET 10's `dotnet new sln` default) references both
`Goto/Goto.csproj` and `Goto.Tests/Goto.Tests.csproj`. `build.ps1` still publishes `Goto/Goto.csproj`
directly, not the solution (publishing the test project via AOT would be meaningless).

## Key design decisions (Milestone 2)

- **No alias lookup yet.** `aliases.tsv`'s format is pinned down but has no writer until Milestone 6
  (`--alias`/`--unalias`) — an always-empty file checked on every resolve would be dead code.
- **Ambiguous matches → exit 2 + stderr list, never a silent guess.** No picker exists yet (M3); see
  `docs/specs/milestone-02-walker-resolver-decisions.md` for the full rationale.
- **`Resolver.Resolve(args, searcherOverride?, configOverride?)`** takes optional DI seams
  specifically for testability — don't remove them, and don't add more without a concrete test need.
- Full list of M2-specific decisions (walker algorithm, exclude-list test gotcha, qualifier-matching
  scope, timestamp format, etc.) is in `docs/specs/milestone-02-walker-resolver-decisions.md` — this
  file only lists the ones that affect how you should modify the code going forward.

## Key design decisions (Milestone 1)

- **ArgParser only recognizes `--`, `--init <value>`, `--install`.** Any other `-x` flag is a hard
  usage error (exit 4), not silently mis-handled — future milestones (`-l`, `-p`, `--refresh`,
  `--explain`, etc.) add their own recognized flags when they're actually implemented.
- **`ArgParser.Positionals` stays raw, unsplit** (no splitting on `/`/`\` within a single arg) —
  `Resolver`'s step 0 path shortcut still needs `C:\Windows` as one token. Splitting on `/`/`\` into
  search terms happens in `GotoQuery.FromPositionals` (Milestone 2), only reached once step 0 fails.
- **Bare `-`** is not treated as a flag or special-cased in the exe at all. The PowerShell wrapper
  intercepts `gt -` before ever calling `goto.exe`; if the exe is invoked directly with a lone `-`,
  it just falls through as an ordinary (non-matching) positional term → exit 1.
- **`Environment.ProcessPath`**, not `Assembly.GetExecutingAssembly().Location`, is used to find the
  running exe's own path (for `--install`) — the latter returns an empty string under Native AOT.
- **`--install`** targets Windows PowerShell 5.1's profile only (`Documents\WindowsPowerShell\
  Microsoft.PowerShell_profile.ps1`), not `pwsh` 7's. Known gap, not yet addressed.
