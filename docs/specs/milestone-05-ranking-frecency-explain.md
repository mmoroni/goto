# Milestone 5: Ranking, frecency, `--explain`, prompt hook

**Status: Implemented.**

## Scope (from design spec §12)

> Ranking, frecency, `--explain`, prompt hook.

## What this covers from the full spec

- **§9 in full** — the ranking formula (frecency term via `10 * log2(1 + visits * recencyFactor)`,
  +30 under cwd, +15 under cwd's parent, +15 under `%USERPROFILE%`, +10 project-root heuristic
  checking only the top 20 candidates, −1.5×depth), the `recencyFactor` bucket scheme (×4/×2/×0.5/×0.25
  for last-hour/day/week/older — zoxide's scheme), aging (halve-ish: ×0.9 and drop sub-1 entries once
  total visits exceed ~10,000), and the existence-check rule (only `Directory.Exists` the top ~50
  ranked candidates, never stat the full result set). These scores are what Milestone 3's
  dominant-winner margin and picker ordering actually consume — this milestone makes that real
  rather than placeholder.
- **§6** — `visits.tsv` becomes load-bearing (it exists as a format from Milestone 2, but ranking is
  what reads and ages it now). `config.txt`'s `dominance_margin` key becomes load-bearing.
- **§4, §11** — `--explain` CLI contract row and feature (Phase 5): show source (decision / tier /
  backend), and the per-candidate score breakdown, term by term. Frecency learning and context
  awareness (both Phase 5) are direct consequences of wiring §9 in: every successful `goto` upserts
  `visits.tsv` (already happening since Milestone 2's step 6, but now the increments actually affect
  future ranking). Project awareness (Phase 5) is the `.git`/`*.sln`/`package.json`/`Cargo.toml`
  repo-root heuristic from the ranking formula's +10 term.
- **§5** — the optional `prompt` hook: wrap PowerShell's `prompt` function so that when `$PWD`
  differs from the last-seen value, it runs `& $global:GOTO_EXE --add $PWD.Path` — meaning plain
  `cd` also builds frecency, not just `goto`. Must save/restore `$LASTEXITCODE` inside the hook.
  This is the mechanism behind the `--add PATH` CLI contract row (§4): record a visit, print
  nothing.
- **§13** — acceptance test 5's "`--explain` reports source `decision`" clause is really completed
  here (Milestone 2 can report *something*, but the full score-breakdown contract is this
  milestone's).

Explicitly **not** in scope: anything from §11 marked Phase 6/7 (aliases, completion, typo
tolerance, abbreviations, `--wrong`, open-elsewhere, Ctrl+G hotkey).

## Acceptance criteria

- Recently/frequently visited folders rank above older/rarer ones when multiple candidates tie on
  tier.
- A folder under the current directory (or its parent, or `%USERPROFILE%`) outranks an equally-named
  folder elsewhere, per the formula's locality bonuses.
- `goto --explain <term>` prints the resolution source and a term-by-term score breakdown for each
  candidate considered.
- After enabling the prompt hook, running plain `cd` (not `goto`) into a folder and later running
  `goto <that folder's name>` reflects the visit in ranking — without ever having called `goto` to
  get there.
- Visit-count aging kicks in once total visits cross ~10,000 (verify via a seeded `visits.tsv` in
  tests rather than actually visiting 10,000 times).

## Implementation notes (decisions made beyond what the spec states)

- **Project-root "top 20" pre-sort uses the cheap partial score** (frecency + locality + depth,
  everything except the project-root bonus itself) to pick which 20 candidates pay the
  filesystem-touching check. Accepted approximation: a candidate ranked #21 by partial score with a
  `.git` folder never gets its +10 and can't out-rank #20 — matches spec's own "starting weights...
  tune them" framing.
- **Existence-check (top ~50) runs after ranking, before any count-based branch**, uniformly for
  `--list`/`--pick`/normal. A phantom (stale Everything) candidate is dropped, not backfilled from
  beyond rank 50 — verified with a dedicated test (`Resolve_PhantomTopRankedCandidate_...`) where a
  heavily-visited never-created path would otherwise rank #1.
- **All-phantom result → `NoMatch`**, not a fallback to the next tier. Rare edge case, not worth
  restructuring the tier loop to re-enter.
- **`--pick` never auto-resolves on a dominant-winner score gap** — only on a literal single
  candidate. Dominant-winner and `--pick`'s forced-decision are separate branches in `Resolver.cs`
  by construction, so there's no shared code path that could accidentally let one leak into the
  other.
- **`--add PATH` always calls `VisitStore.Increment` unconditionally**, no existence check, swallows
  path-parsing exceptions silently, always exits 0 with no stdout.
- **Prompt hook is opt-in via `--with-prompt-hook`** (on both `--init powershell` and `--install`),
  not unconditional — the base wrapper script stays byte-identical to the spec's verbatim text
  either way. Confirmed manually: `--init powershell` alone is unchanged; `--with-prompt-hook`
  appends the hook; `--install --with-prompt-hook` writes the hook-enabled line.
- **Known gap found during manual testing**: `--install`'s idempotency check only looks for the
  marker comment, not its content — so re-running `--install --with-prompt-hook` against a profile
  that already has a non-hook goto block silently no-ops instead of upgrading it to include the
  hook. Not fixed this milestone (not part of the original scope, and the fix — comparing desired
  vs. actual block content — adds real complexity to a currently simple idempotency check); the
  workaround is `--forget`-style manual removal of the existing block (once Milestone 6 or later
  adds such a command) or hand-editing the profile.
- **Shared `Canonicalize` extraction**: `Goto/Store/PathCanon.cs` now holds the logic previously
  duplicated byte-for-byte in `DecisionStore.cs` and `VisitStore.cs`; `Ranker.FrecencyTerm` uses it
  too when looking up a candidate's visit record, so lookups use exactly the same canonical key
  `VisitStore.Increment` wrote under.
- **`--explain` always navigates** (prints the path to stdout, wrapper still `cd`s) — it's a
  diagnostic overlay on stderr, printed from a single call site right after `Resolver.Resolve`
  returns, not threaded into every switch branch.

## Key files

- `Goto/Core/Ranker.cs`, `Goto/Core/Suggestions.cs` (unchanged, now fed ranked-not-alphabetical input)
- `Goto/Store/PathCanon.cs` (new, extracted from `DecisionStore.cs`/`VisitStore.cs`)
- `Goto/Store/VisitStore.cs` (reader + aging), `Goto/Store/Config.cs` (`dominance_margin`)
- `Goto/Core/Resolver.cs` (full step-5 rewrite, `ExplainTrace`/`ScoreBreakdown`, `BackendError`
  reused for ranking's `NoMatch`-on-all-phantom case)
- `Goto/Program.cs` (`PrintExplain`, `RunAdd`, `RunInit`/`RunInstall` gain `withPromptHook`)
- `Goto/Shell/PowerShellInit.cs` (`PromptHookAddendum`)
- `Goto.Tests/Core/RankerTests.cs`, `Goto.Tests/Store/VisitStoreTests.cs`, extended
  `ResolverTests.cs`/`ConfigTests.cs`/`ArgParserTests.cs`
