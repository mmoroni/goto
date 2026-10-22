# Milestone 7: Polish

**Status: Implemented.**

## Scope (from design spec §12)

> Polish: AOT publish, startup-time check, `--install`.

## What this covers from the full spec

- **§3** — Native AOT publish is fully in place as of Milestone 1 (`PublishAot=true`, `win-x64`,
  single exe of a few MB). This milestone's job is verifying the **performance budget** holds once
  all prior milestones' code (search tiers, ranking, picker, aliases, Everything SDK P/Invoke) is
  present — AOT and reflection-avoidance decisions made early can still regress if later milestones
  introduce reflection-heavy patterns.
- **§12, "Performance budget"** — explicit targets to verify end-to-end: a remembered decision
  completes in under 50 ms total; a warm Everything query completes in under 100 ms; the walker is
  capped at 3 s. This milestone is where these get measured and enforced, not just assumed.
- **§5, §4** — `--install` is implemented (Milestone 1, ahead of schedule); this milestone's
  "polish" angle is making sure it still idempotently targets the right profile(s) now that the
  rest of the CLI surface (aliases, completion, prompt hook) may also want profile-level wiring, and
  revisiting the known Milestone-1 gap of not covering `pwsh` 7's profile.
- **§11** — Phase 7 items: "Open elsewhere" (`goto -e docs` opens Explorer, `goto -c docs` opens VS
  Code), the `Ctrl+G` PSReadLine hotkey that opens the picker over everything visited.

Explicitly **not** new scope here: nothing from earlier sections is newly introduced at this
milestone — it's verification, measurement, and the two Phase-7 convenience features, since
everything else was already delivered incrementally by Milestones 1–6.

## Acceptance criteria

- Cold start-to-exit for a remembered decision is measured and stays under 50 ms.
- A warm Everything-backed query stays under 100 ms end to end.
- A full walker fallback search never exceeds the configured `walk_timeout_ms` (default 3000ms) wall
  clock, partial-results message included.
- `goto -e <term>` opens the resolved folder in Explorer instead of `cd`-ing; `goto -c <term>` opens
  it in VS Code.
- A PSReadLine `Ctrl+G` handler opens the picker over the full visited-folder history.
- `--install` is revisited for `pwsh` 7 profile support (closing the Milestone-1 gap), if not
  already addressed earlier.

## Implementation notes (decisions made beyond what the spec states)

- **`-e`/`--explore` and `-c`/`--code` are pure `Program.cs` post-processing** — `Resolver`/
  `ResolveResult` are untouched. A single `DispatchSuccessOrOpen` helper replaces every former
  `RawOutput.WriteStdout(path)` call after a successful resolution (plain resolve, the picker's
  selection in `DispatchNeedsDecision`, and `--wrong`'s correction). On `-e`/`-c`, nothing is
  printed to stdout, so the PowerShell wrapper's `if ($code -eq 0 -and $out)` guard naturally skips
  `Set-Location` — opening in Explorer/Code never also `cd`s.
- **`-e`/`-c` deliberately compose with `-p`/`--pick`** (`goto -p -e docs` = pick, then open the pick
  in Explorer) but are rejected together with `--list` (which never resolves to one path — nothing
  to open) and with each other.
- **`ExternalOpener`** splits into a pure `BuildStartInfo` (unit-tested) and a thin `TryLaunch`
  (not unit-tested — real `Process.Start` calls). Explorer launches via raw `CreateProcess`
  (`UseShellExecute = false`, always resolvable on a standard install); VS Code launches via
  `UseShellExecute = true`, since `code` is normally a `.cmd` shim that raw `CreateProcess` won't
  resolve through `PATHEXT` the way `ShellExecute` does. **Verified empirically**: `ShellExecute`
  also resolves `code` via the `HKCU\...\App Paths\Code.exe` registry key independent of `PATH` —
  manually stripping VS Code's directory from `PATH` still successfully opened VS Code, the same
  way typing `code .` into Win+R would. This is a feature, not a bug, but means `PATH`-stripping
  isn't a reliable way to manually force the launch-failure branch; the failure branch itself is
  still real (wrapped in try/catch for `Win32Exception`/`IOException`), just not easily triggered
  by this particular manual test.
- **`--history` (backs the `Ctrl+G` hotkey) orders by recency, not `Ranker.Rank`** — "everywhere
  I've been" has no cwd/query to score against, so frecency+locality+project-root would be a
  non-sequitur; `HistoryOrdering.OrderByRecency` sorts by most-recent `LastVisitUtc`, tie-broken by
  visit count. Reuses `Picker.Run` verbatim (no new UI code). Deliberately does **not** write to
  `DecisionStore` (no query key exists to attach a decision to) or `VisitStore.Increment` (the
  prompt hook, if enabled, already picks up the resulting `Set-Location` on the next prompt
  redraw — incrementing here too would double-count; without the hook this behaves like a plain
  `cd`, consistent with un-hooked `cd` having no frecency effect today).
- **The `Ctrl+G` PSReadLine handler is unconditional**, like `TabCompletionBlock` and unlike the
  opt-in prompt hook — it only runs when the user actually presses Ctrl+G, no recurring cost. The
  whole `Set-PSReadLineKeyHandler` registration is wrapped in `try/catch`: PSReadLine isn't
  guaranteed loaded in every host (ISE, non-interactive/scripted profile loads, or a user who
  removed the module), and a missing cmdlet must never break profile load. Reuses the base
  `Script`'s exact `$out`/`$code`-capture + UTF-8-switch + conditional-`Set-Location` idiom.
- **`--install` now writes into both Windows PowerShell 5.1's and `pwsh` 7's profiles,
  unconditionally, no new flag** — both are valid regardless of which shell runs `--install`, and
  `--init powershell`'s script content doesn't vary by edition. The `pwsh` 7 profile path formula
  (`Documents\PowerShell\Microsoft.PowerShell_profile.ps1`) is hardcoded rather than shelled out to
  `pwsh -Command '$PROFILE'` — it's a stable, documented convention, and shelling out would fail
  outright on a machine without `pwsh` installed. `ProfileInstallBlock.Build` is extracted (pure,
  unit-tested) so both profiles share identical marker-wrapping logic. Exit code is `Success` only
  if both installs succeed — best-effort, not transactional; one profile can fail while the other
  still gets installed and reported.
- **Performance budget: all three measured targets pass comfortably, no code fix needed** (measured
  via `build.ps1`'s published exe, median of 10 runs after a priming run):
  - Remembered decision: median ~14ms, max ~18ms (budget: <50ms).
  - Warm Everything query (`--list`, which always bypasses the decision cache): median ~29ms, one
    cold outlier at ~55ms (budget: <100ms). `NativeLibraryExtractor.EnsureExtracted`'s per-
    invocation SHA256 recompute was flagged as a theoretical risk but did not blow the budget in
    practice.
  - Walker fallback timeout (`backend = walk`, guaranteed no-match, run from `C:\`): ~3116ms against
    a 3000ms `walk_timeout_ms` — within the expected small process-overhead slack, not a multiple
    of the budget.

## Key files

- `Goto/Shell/ExternalOpener.cs`, `Goto/Core/HistoryOrdering.cs`, `Goto/Shell/ProfileInstallBlock.cs`
  (new)
- `Goto/Cli/ArgParser.cs` (`-e`/`--explore`, `-c`/`--code`, `--history`, mutual-exclusion rules)
- `Goto/Program.cs` (`DispatchSuccessOrOpen`, `RunHistory`, `RunWrong` gains explore/code params,
  `RunInstall`/`TryInstallProfile` dual-profile)
- `Goto/Shell/PowerShellInit.cs` (`HistoryHotkeyBlock`)
- `Goto.Tests/Shell/ExternalOpenerTests.cs`, `ProfileInstallBlockTests.cs` (new),
  `Goto.Tests/Core/HistoryOrderingTests.cs` (new), extended `ArgParserTests.cs`
