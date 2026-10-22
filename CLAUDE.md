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
the index and each milestone's status. **This repo fully implements Milestones 1-7** (skeleton;
walker + resolver + decisions store; picker + "too many" handling; Everything backend; ranking +
frecency + `--explain` + prompt hook; aliases + completion + typo/abbreviation tiers + `--wrong`;
open-elsewhere + Ctrl+G history hotkey + dual-profile `--install` + measured performance budget) —
the full original design spec's 7-milestone build order is complete. When a milestone ships, update
its status and add implementation notes in its spec file, following the pattern in
`milestone-01-skeleton.md` through `milestone-07-polish.md`. Changes made after the build order
completed (not called for by the original spec, e.g. `-h`/`--help`) get their own
`docs/specs/addendum-0N-*.md` instead — see `docs/specs/README.md`.

`Everything64.dll` (`Goto/Search/Native/Everything64.dll`) is redistributed under the voidtools SDK's
MIT-style license — see `docs/third-party/Everything-SDK-LICENSE.txt`. It's embedded as a resource,
not shipped alongside the exe.

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
on this: it captures `$out = & $GOTO_EXE @args` and does `Set-Location -LiteralPath (@($out)[0])`
whenever exit code is 0 **and** `$out` is non-empty.

**That last clause is the trap**: any command reachable via `gt`/`goto` (not just direct term
resolution) that prints human-facing text to stdout and exits 0 will make the wrapper try to `cd`
into that text. `RunDoctor`, `RunAliasesList`, and `RunHelp` all hit this bug in production before
it was caught — see `docs/specs/addendum-02-stdout-discipline-bugfixes.md`. **Any new meta-command
reachable via the wrapper must print to `Console.Error`, never `Console.WriteLine`/`Console.Out`,
even though it isn't "resolving a term."** The only commands that may legitimately write to real
stdout are: (a) a resolved path, via the `RawOutput.WriteStdout` chokepoint below; (b) `--init`'s
script text, same chokepoint, consumed by `Invoke-Expression`, a different contract entirely; and
(c) `--complete`, via plain `Console.WriteLine` — safe only because it's invoked directly by
`Register-ArgumentCompleter`'s script block, never through the `goto`/`gt` wrapper function.

`Shell/RawOutput.WriteStdout` uses `Console.OpenStandardOutput()` directly — never `Console.Out` /
`Console.WriteLine` — specifically for resolved paths and `--init`'s script text, to avoid Windows
console-codepage mangling of non-ASCII paths. This is a stricter requirement than "goes to stdout"
in general; `--complete`'s plain `Console.WriteLine` bypasses it because visited-folder leaf names
don't have the same mangling risk as full paths in the Everything/walker search paths, and it's
simpler for the one case that genuinely needs ordinary line-buffered stdout.

`--init` and `--install` are meta-commands, not resolutions — `--init powershell`'s script-text
output legitimately goes to stdout too (via the same `RawOutput` chokepoint), since it's consumed
by `| Invoke-Expression`, a different contract entirely. These are mutually exclusive branches in
`Program.cs` from the resolution path, so the two contracts never collide.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success, path on stdout |
| 1 | No match |
| 2 | Too many matches, ambiguous (non-interactive dump), or picker cancelled (Esc/Ctrl+C) |
| 3 | Backend or config error (incl. `backend = everything` configured but unreachable) |
| 4 | Usage error |

## Layout

```
Goto/Program.cs              entry point, exit-code dispatch, --init/--install handlers, picker dispatch
Goto/Cli/ArgParser.cs         argv -> ParsedArgs (terms, --, --init, --install, --list, --pick, --no-ui,
                              -e/--explore, -c/--code, --history, -h/--help)
Goto/Cli/HelpText.cs          -h/--help full text (stdout, exit 0) + the terse bare-usage-error line
Goto/Core/ExitCode.cs         exit code constants
Goto/Core/PathShortcut.cs     single-term existing-path check (., .., ~, C:\x, relative dirs)
Goto/Core/GotoQuery.cs        term splitting (/ and \ within an arg) + leaf/qualifiers/decision key
Goto/Core/Matcher.cs          tier leaf-match (T1-T4, incl. abbreviation) + qualifier ancestor-match
Goto/Core/Ranker.cs           frecency + locality + project-root + depth scoring (pure, DI'd inputs)
Goto/Core/EditDistance.cs     Levenshtein distance, for typo-tolerance "did you mean" suggestions
Goto/Core/Resolver.cs         full pipeline: path shortcut, alias, decision, tiers, rank, upsert
Goto/Core/Suggestions.cs      "be more specific" ancestor-split suggestions for TooMany
Goto/Search/IFolderSearcher.cs  backend interface (WalkSearcher and EverythingSearcher both implement)
Goto/Search/WalkSearcher.cs   fallback filesystem walker
Goto/Search/EverythingSearcher.cs   SDK-backed searcher (auto-starts/polls Everything.exe)
Goto/Search/EverythingNative.cs     P/Invoke declarations, verified against the real Everything.h
Goto/Search/EverythingQueryBuilder.cs  pure query-string builder (exact/prefix/contains + qualifiers)
Goto/Search/NativeLibraryExtractor.cs  lazy embedded-DLL extraction + hash-checked re-extraction
Goto/Search/BackendSelector.cs      auto/everything/walk selection, forced-mode hard-error path
Goto/Search/EverythingFallbackHint.cs  rate-limited (30min) stderr hint on auto-fallback
Goto/Search/Native/Everything64.dll  embedded resource (MIT-licensed, see docs/third-party/)
Goto/Store/DataPaths.cs       GOTO_HOME / portable-mode / %LOCALAPPDATA% resolution
Goto/Store/AtomicFile.cs      named-mutex + tmp-then-rename write primitive
Goto/Store/PathCanon.cs       shared path canonicalization (Decision/Visit/Alias stores + Ranker)
Goto/Store/Config.cs          config.txt parser (exclude / walk_roots / walk_timeout_ms / max_candidates / backend / everything_path / dominance_margin)
Goto/Store/DecisionStore.cs   decisions.tsv read/write + stale-entry cleanup
Goto/Store/VisitStore.cs      visits.tsv read (LoadAll) + increment + aging (>10k total -> x0.9, drop <1)
Goto/Store/AliasStore.cs      aliases.tsv read/write (name<TAB>path, case-insensitive name lookup)
Goto/Store/LastResolutionStore.cs  last.txt: single-row "most recent resolution" for --wrong
Goto/Shell/PowerShellInit.cs  --init powershell script text (verbatim base — don't reword/retune)
                              + unconditional TabCompletionBlock + unconditional HistoryHotkeyBlock
                              (Ctrl+G) + optional PromptHookAddendum
Goto/Shell/RawOutput.cs       byte-exact stdout writer (the only stdout writer in the program)
Goto/Shell/ExternalOpener.cs  -e/-c: pure ProcessStartInfo builder + Process.Start launcher
Goto/Shell/ProfileInstallBlock.cs  shared marker-wrapped block builder for --install's dual profiles
Goto/Core/HistoryOrdering.cs  pure recency ordering for --history (backs the Ctrl+G hotkey)
Goto/Ui/Vt.cs                 VT escape processing scope for stderr (P/Invoke console mode)
Goto/Ui/PickerState.cs        pure, console-free picker state machine (unit-testable)
Goto/Ui/Picker.cs             the only code that touches Console.ReadKey/real rendering
```

`goto.slnx` (note: `.slnx`, not `.sln` — .NET 10's `dotnet new sln` default) references both
`Goto/Goto.csproj` and `Goto.Tests/Goto.Tests.csproj`. `build.ps1` still publishes `Goto/Goto.csproj`
directly, not the solution (publishing the test project via AOT would be meaningless).

## Key design decisions (Milestone 7)

- **`-e`/`-c` and `--history` are pure `Program.cs` post-processing** — `Resolver`/`ResolveResult`
  never learn about "explore"/"code" mode. A single `DispatchSuccessOrOpen` helper is the only place
  that decides whether a resolved path gets printed to stdout (for the wrapper's `Set-Location`) or
  launched externally — every code path that used to call `RawOutput.WriteStdout(path)` after a
  successful resolution routes through it now.
- **`-e`/`-c` compose with `-p`/`--pick`** (pick, then open the pick) but are rejected together with
  `--list` (never resolves to one path) and with each other. Don't add new flags to this family
  without deciding explicitly whether they're "resolve to one path" actions (composable with `-p`)
  or "dump everything" actions (composable with `--list` instead).
- **VS Code's `code` resolves via the `HKCU\...\App Paths\Code.exe` registry key independent of
  `PATH`** when launched with `UseShellExecute = true` — confirmed empirically (stripping VS Code's
  directory from `PATH` still successfully launched it). This is why `code` uses
  `UseShellExecute = true` while `explorer.exe` uses `false` — don't "simplify" them to match.
- **`--history` never writes `VisitStore`/`DecisionStore`** — browsing history has no query key to
  attach a decision to, and incrementing the visit count again would double-count what the prompt
  hook (if enabled) already records on the next `Set-Location`.
- **`--install` writes into both Windows PowerShell 5.1's and `pwsh` 7's profiles unconditionally**
  — the Milestone-1-documented gap is closed. Both profile paths are always attempted regardless of
  which shell is running `--install`; failure in one doesn't block the other (best-effort, not
  transactional).
- Full list of M7-specific decisions (including measured performance numbers) is in
  `docs/specs/milestone-07-polish.md`.

## Key design decisions (Milestone 6)

- **PowerShell silently drops a literal empty-string argument when invoking a native exe, in ANY
  position** — confirmed empirically, not just a trailing-argument quirk. This breaks naive tab
  completion (`$wordToComplete` is `""` right after `goto ` with nothing typed). Fixed with a
  single-token `--complete=WORD` form in `ArgParser` (never empty as a whole token, even when `WORD`
  is) — the real completer script uses `"--complete=$wordToComplete"`, not two separate arguments.
  **If you add any other flag whose value might legitimately be an empty string and gets invoked from
  a generated PowerShell script, use this same `--flag=value` pattern, not two-argument `--flag value`.**
- **`EverythingQueryBuilder` must handle every `MatchMode` the real search path can reach** — it
  previously threw for `Abbreviation` and a test asserted that, which would have crashed in
  production the moment T4 became reachable. When adding a new tier/mode to `Resolver.Tiers`, always
  check `EverythingQueryBuilder.Build`'s switch handles it (not just `Matcher.LeafMatches`).
- **A plain single-character-deletion typo very often still matches via T4 Abbreviation** (removing
  letters preserves subsequence order), so typo-tolerance's "did you mean" only reliably fires for
  typos that break order (transpositions, etc.) — this is expected, not a bug if a simple typo just
  silently resolves instead of prompting.
- **ALIAS lookup (step 1) never existence-checks or auto-cleans stale entries**, unlike the decision
  cache — a manual bookmark, not an opportunistic cache. Don't add a `Directory.Exists` guard there
  without reconsidering this deliberately.
- Full list of M6-specific decisions is in `docs/specs/milestone-06-extras.md`.

## Key design decisions (Milestone 5)

- **`Ranker` is pure** — `cwd`/`userProfile`/`nowUtc` are parameters, never read from ambient state
  inside `Ranker` itself; `Resolver` supplies the real values at the call site. Keep it this way for
  testability.
- **`--pick` and dominant-winner are separate branches in `Resolver.Resolve`**, not shared code —
  `--pick` never auto-resolves on a score gap, only a literal single candidate. Don't refactor these
  into one combined "maybe auto-resolve" path without preserving that distinction.
- **Existence-check (top ~50) happens after ranking, before any count-based branch** (1-match /
  dominant-winner / `NeedsDecision`) — a phantom top-ranked candidate must be dropped before deciding
  ambiguity, not after. Getting this ordering wrong silently changes resolution outcomes.
- **`--install`'s idempotency check doesn't detect hook-vs-no-hook drift** — re-running `--install
  --with-prompt-hook` against an already-installed-without-hook profile silently no-ops instead of
  upgrading. Known, documented gap (see `docs/specs/milestone-05-ranking-frecency-explain.md`), not a
  regression to "fix" reflexively without considering the idempotency-check redesign it implies.
- Full list of M5-specific decisions (ranking formula judgment calls, aging truncation behavior,
  etc.) is in `docs/specs/milestone-05-ranking-frecency-explain.md`.

## Key design decisions (Milestone 4)

- **Forced `backend = everything` but unreachable → hard error, exit 3**, not a silent walker
  fallback (`auto` mode falls back silently; forcing `everything` and quietly substituting the
  walker would be misleading about what's actually happening).
- **`walk_roots` only scopes the walker** — `EverythingSearcher` ignores it entirely (it queries
  Everything's whole index directly). Don't expect `backend = auto`/`everything` test scenarios to
  respect a scoped `walk_roots`; only `backend = walk` does.
- **`--doctor` calls `EverythingSearcher.TryConnect` exactly once** — calling it a second time (e.g.
  via a separate `BackendSelector.Select` call) risks launching Everything.exe twice if its DB is
  still loading when the second call starts its own independent poll. Found via manual testing, not
  by inspection — don't reintroduce a second connect attempt in diagnostic/reporting code.
- Full list of M4-specific decisions (SDK verification process, AOT resolver confirmation, etc.) is
  in `docs/specs/milestone-04-everything-backend.md`.

## Key design decisions (Milestone 3)

- **`--pick` auto-resolves a single match instead of forcing a one-row picker**, in any context
  (interactive or not) — keeps `--pick` usable under `--no-ui`/redirected input. Multiple matches
  still force the picker (or a non-interactive dump).
- **`Console.TreatControlCAsInput = true` is required around the picker's read loop** (restored
  after) or `Console.ReadKey` never sees Ctrl+C — the runtime's default handler intercepts it first.
- **Scripted verification against this exe must capture stdout via `$out = & $exe args`** (with
  `[Console]::OutputEncoding` forced to UTF-8 first), **never** `1> file` redirection — the latter
  round-trips a native process's stdout through PowerShell's own string pipeline, corrupting bytes
  (re-encoding, adding CRLF). This is the same gotcha the real PowerShell wrapper already works
  around; don't reintroduce it in test/verification scripts.
- Full list of M3-specific decisions is in `docs/specs/milestone-03-picker-and-too-many.md`.

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
- **`--install`** targeted Windows PowerShell 5.1's profile only (`Documents\WindowsPowerShell\
  Microsoft.PowerShell_profile.ps1`), not `pwsh` 7's, at this milestone. Closed in Milestone 7 (see
  above) — `--install` now writes into both profiles.
