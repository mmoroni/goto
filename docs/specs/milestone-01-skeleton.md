# Milestone 1: Skeleton

**Status: Implemented.**

## Scope (from design spec §12)

> Skeleton: args, exit codes, path shortcut, `--init`, wrapper.
> Done when `gt ..` and `gt C:\Windows` work in a fresh terminal.

## What this covers from the full spec

- **§1A** — the constraint this whole project exists to solve (child process can't `cd` its parent shell).
- **§3** — tech stack and project setup (csproj PropertyGroup, Native AOT, project layout).
- **§4** — CLI contract, but only these rows: `goto <term> [<term>…]` (path-shortcut case only — no
  search tiers yet), `goto -` (handled entirely in the wrapper, not the exe), `--init powershell`,
  `--install`, `--` (end of options). Exit code table in full.
- **§5** — PowerShell integration: the wrapper script verbatim, and all its gotchas (don't pipe to
  `Select-Object -First 1` before reading `$LASTEXITCODE`; raw UTF-8 stdout; execution-policy hint).
- **§7, step 0 only** — PATH SHORTCUT: single term that's an existing path (`.`, `..`, `~`, `C:\x`,
  relative dir) → print it. Steps 1–6 (alias, decision, tiers, ranking) are out of scope.
- **§13 edge cases**, the subset reachable without a search backend: folders with spaces, non-ASCII
  names (UTF-8 end to end — acceptance test 8, `Café`), `--` for folders starting with `-`.

Explicitly **not** in scope: Store/, Search/, Ui/ directories; decisions/visits/aliases/config
files; search tiers; ranking; the picker; the Everything backend.

## Acceptance criteria

- `gt ..` and `gt C:\Windows` work in a fresh PowerShell terminal after `--init`/`--install`.
- `goto.exe --init powershell` prints the wrapper script verbatim to stdout.
- `goto.exe --install` idempotently appends one line to the Windows PowerShell 5.1 profile and
  prints the execution-policy hint.
- Exit codes 0/1/4 are reachable (2 and 3 aren't — no picker, no backend yet).
- stdout is byte-exact (no trailing newline/BOM) and carries only the resolved path; stderr carries
  everything else.

## Implementation notes (decisions made beyond what the spec states)

- **ArgParser only recognizes `--`, `--init <value>`, `--install`.** Every other flag (`-l`, `-p`,
  `--refresh`, etc.) is a hard usage error (exit 4) until its own milestone implements it, rather
  than being silently stubbed out now.
- **Positional args are collected raw, unsplit.** Spec §4 says `/`/`\` inside an arg splits it into
  terms, but §7 step 0 tests `C:\x` as a single token — splitting would break that. Splitting is
  deferred to whichever milestone adds qualifier/leaf tiered matching (Milestone 2).
- **Bare `-`** gets no special case in the exe — the wrapper intercepts `gt -` before calling
  `goto.exe` at all; direct invocation of `goto.exe -` just falls through to `NoMatch` (exit 1).
- **`Environment.ProcessPath`**, not `Assembly.GetExecutingAssembly().Location`, for finding the
  running exe's own path (needed by `--install`) — the latter is empty under Native AOT.
- **`--install`** targets only Windows PowerShell 5.1's profile
  (`Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1`), not `pwsh` 7's. Known gap.
- Native AOT publish on this machine required installing the VS Build Tools "Desktop development
  with C++" workload (for `link.exe`) and routing the build through `VsDevCmd.bat`'s environment
  plus the VS Installer directory on `PATH` (for `vswhere.exe`, which the ILCompiler targets shell
  out to directly) — see `build.ps1` at the repo root.

## Key files

- `Goto/Program.cs`, `Goto/Cli/ArgParser.cs`, `Goto/Core/ExitCode.cs`, `Goto/Core/PathShortcut.cs`,
  `Goto/Core/Resolver.cs`, `Goto/Shell/PowerShellInit.cs`, `Goto/Shell/RawOutput.cs`
