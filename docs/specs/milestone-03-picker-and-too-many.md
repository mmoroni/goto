# Milestone 3: Picker and "too many" handling

**Status: Implemented.**

## Scope (from design spec §12)

> Picker and "too many" handling: includes non-interactive mode.

## What this covers from the full spec

- **§4** — CLI contract rows: `-l, --list` (show ranked candidates on stderr, don't navigate),
  `-p, --pick` (ignore remembered decision, force picker, remember the choice). Exit code 2
  (ambiguous/too many/picker cancelled) becomes reachable for the first time.
- **§7, step 5** — the decision logic once multiple matches exist: 1 match → go; dominant winner
  (top score − 2nd ≥ margin) → go; count ≤ `max_candidates` → picker; else → "too many" + exit 2.
  Note: a real score margin needs ranking (§9, Milestone 5) — until then, this milestone can use a
  simplified placeholder ordering (e.g. by path length or alphabetical) to make the picker/too-many
  branch testable, and swap in real scores once Milestone 5 lands.
- **§7** — "Non-interactive safety": if `Console.IsInputRedirected` or `--no-ui` is passed, never
  show the picker — print candidates to stderr and exit 2 instead.
- **§10** — full Picker UI spec: draw to stderr with ANSI/VT escapes (`ENABLE_VIRTUAL_TERMINAL_PROCESSING`
  via `GetConsoleMode`/`SetConsoleMode`), `Console.ReadKey(intercept: true)`, keybindings (↑/↓, Enter,
  Esc/Ctrl+C → exit 2, 1–9 jump, typing filters), row rendering (highlighted matched leaf + dim
  annotation), in-place redraw, cursor restore in `try/finally` including on exceptions.
- **§13** — acceptance tests 3 (40 matches, no clear winner → "be more specific" + exit 2) and 9
  (picker cancelled with Esc → exit 2, no `cd`).

Explicitly **not** in scope: the "be more specific" suggestion *algorithm* itself (computing which
ancestor names split the candidate set) — that's listed as Phase 4 in §11, bundled with the
Everything-backend milestone; M3 can ship "too many" with just a plain truncated candidate list and
add the smarter suggestions in Milestone 4. Real ranking scores (§9) are still Milestone 5 — this
milestone consumes whatever ordering exists at the time.

## Acceptance criteria

- With multiple ambiguous matches under `max_candidates`, the interactive picker appears on stderr,
  stdin/stdout are otherwise untouched, and selecting an entry prints its path and exits 0.
- Esc or Ctrl+C in the picker exits 2 with no path printed and no `cd` performed.
- With input redirected (or `--no-ui`), the picker never appears; candidates print to stderr and
  the process exits 2.
- More than `max_candidates` matches with no dominant winner → "Too many (N). Be more specific."
  on stderr, exit 2.
- `-l`/`--list` shows ranked candidates on stderr without navigating (exit code TBD — spec doesn't
  pin this down explicitly; treat as exit 0 if listing succeeded, matching "no error" semantics).

## Implementation notes (decisions made beyond what the spec states)

- **`-p`/`--pick` with exactly one verified candidate auto-resolves without showing the picker**,
  regardless of interactivity. The literal spec wording ("force the picker... regardless of count")
  would make `goto --pick <unambiguous-term>` fail under `--no-ui`/redirected input, since there'd be
  nothing to show a picker *with*. `--pick`'s real functional value — skip the decision cache, let the
  user override/refresh it — is fully satisfied by bypassing the cache and re-searching; a one-row
  confirmation dialog adds little and makes the non-interactive case needlessly fragile. So `--pick`
  always skips the decision cache, but only forces the picker when there's genuine ambiguity (2+
  candidates); a single match just succeeds (and still records the decision).
- **Digit keys 1-9 jump to that row only while the filter is empty.** Once the user has typed any
  filter character, digits become ordinary filter characters instead — otherwise folder names
  containing digits (e.g. `2024-budget`) could never be filtered down to by typing a digit.
- **`Console.TreatControlCAsInput` must be set to `true` before the picker's read loop and restored
  after.** Without this, `Console.ReadKey(intercept: true)` never actually sees Ctrl+C as a key — the
  runtime's default Ctrl+C handler intercepts it first and tears down the process before the picker's
  own cancel-handling code ever runs. Found this during design, not left as a gap.
- **`-l`/`--list` and `-p`/`--pick` together is a hard usage error** (exit 4) — opposed semantics
  (list never navigates/records, pick always does), so silently preferring one over the other would
  be a surprising, argument-order-dependent behavior.
- **Picker-cancelled (Esc/Ctrl+C) is silent** — exit 2, no stderr message, no stdout, no decision
  recorded. Keeps stderr parseable for scripts watching for the two list-shaped messages (too-many /
  non-interactive-dump) without a third shape to ignore.
- **`TooMany`'s display cap stays a separate hardcoded 20**, independent of the new
  `config.MaxCandidates` (which governs the picker-vs-too-many decision boundary, defaulting to 15
  per the spec's own config.txt example). Different concerns: one is "how many is too ambiguous to
  show a picker for," the other is "how many to print before truncating the stderr dump."
- **The picker's row-rendering omits the §10 "dim annotation"** (e.g. `last used 2d ago · git repo`)
  entirely this milestone — its data sources (a `VisitStore` reader, project-root detection) both
  belong to Milestone 5's ranking work. No placeholder parameter was added either; M5 can extend the
  renderer when it has real data to supply.
- **No `Ui/Log.cs`.** Everything already writes directly to `Console.Error`; a wrapper would add no
  value until there's an actual cross-cutting concern (there isn't one yet).
- **`PickerState` is a pure, console-free state machine** (`Goto/Ui/PickerState.cs`) separated from
  the actual I/O loop (`Goto/Ui/Picker.cs`) specifically so the keybinding logic is unit-testable —
  `Console.ReadKey`/real terminal rendering can't run under `dotnet test`, but `PickerState.HandleKey`
  takes a plain `ConsoleKeyInfo` and can be driven with synthetic key presses.
- **Manual-only verification**: the interactive picker itself (arrow keys, Enter, Esc/Ctrl+C, digit
  jump, type-to-filter, actual terminal rendering) cannot be driven through `dotnet test` or a
  piped/redirected `powershell.exe -Command` session — both lack a real console for `Console.ReadKey`
  to attach to. All other M3 behavior (the `NeedsDecision`/`TooMany` boundary, `--list`, `--pick`'s
  single/multi-match paths, non-interactive dumps, stdout purity) was verified scriptably by
  capturing output the same way the real PowerShell wrapper does — `$out = & $exe args` with
  `[Console]::OutputEncoding` forced to UTF-8 first, **not** `1> file` redirection, which round-trips
  a native process's stdout through PowerShell's own string pipeline (re-encoding it, adding CRLF)
  rather than passing bytes through raw. This is the same gotcha the spec's wrapper script already
  works around — worth remembering for any future scripted verification against this exe.

## Key files

- `Goto/Cli/ArgParser.cs` (`-l`/`--list`, `-p`/`--pick`, `--no-ui`)
- `Goto/Store/Config.cs` (`max_candidates`)
- `Goto/Core/Resolver.cs` (`NeedsDecision`/`Listed` kinds, `RecordChoice`)
- `Goto/Program.cs` (`DispatchNeedsDecision`)
- `Goto/Ui/Vt.cs`, `Goto/Ui/PickerState.cs`, `Goto/Ui/Picker.cs`
- `Goto.Tests/Ui/PickerStateTests.cs`, `Goto.Tests/Cli/ArgParserTests.cs`,
  `Goto.Tests/Store/ConfigTests.cs`, extended `Goto.Tests/Core/ResolverTests.cs`
