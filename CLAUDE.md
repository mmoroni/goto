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
the index and each milestone's status. **This repo currently implements only Milestone 1** (skeleton:
args, exit codes, path shortcut, `--init`, `--install`). Do not add Store/Search/Ui code,
decisions/visits TSV files, ranking, or the Everything SDK integration until asked — those belong to
later milestones and would be half-finished implementations right now. When a milestone ships,
update its status and add implementation notes in its spec file, following the pattern in
`milestone-01-skeleton.md`.

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
| 2 | Ambiguous/too many matches, or picker cancelled (not yet implemented — no picker in M1) |
| 3 | Backend or config error |
| 4 | Usage error |

## Layout

```
Goto/Program.cs              entry point, exit-code dispatch, --init/--install handlers
Goto/Cli/ArgParser.cs         argv -> ParsedArgs (terms, --, --init, --install)
Goto/Core/ExitCode.cs         exit code constants
Goto/Core/PathShortcut.cs     single-term existing-path check (., .., ~, C:\x, relative dirs)
Goto/Core/Resolver.cs         resolution pipeline (path shortcut only in M1)
Goto/Shell/PowerShellInit.cs  --init powershell script text (verbatim — don't reword/retune)
Goto/Shell/RawOutput.cs       byte-exact stdout writer (the only stdout writer in the program)
```

No `.sln` yet — single project, no tests until a later milestone adds `Goto.Tests/`.

## Key design decisions specific to this milestone

- **ArgParser only recognizes `--`, `--init <value>`, `--install`.** Any other `-x` flag is a hard
  usage error (exit 4), not silently mis-handled — future milestones (`-l`, `-p`, `--refresh`,
  `--explain`, etc.) add their own recognized flags when they're actually implemented.
- **Positional args are collected raw, unsplit** (no splitting on `/`/`\` within a single arg).
  Splitting only matters for future tiered-search qualifier/leaf matching — doing it now would
  break the single-term path-shortcut check, since `C:\Windows` must be tested as one token.
- **Bare `-`** is not treated as a flag or special-cased in the exe at all. The PowerShell wrapper
  intercepts `gt -` before ever calling `goto.exe`; if the exe is invoked directly with a lone `-`,
  it just falls through as an ordinary (non-matching) positional term → exit 1.
- **`Environment.ProcessPath`**, not `Assembly.GetExecutingAssembly().Location`, is used to find the
  running exe's own path (for `--install`) — the latter returns an empty string under Native AOT.
- **`--install`** targets Windows PowerShell 5.1's profile only (`Documents\WindowsPowerShell\
  Microsoft.PowerShell_profile.ps1`), not `pwsh` 7's. Known gap, not yet addressed.
