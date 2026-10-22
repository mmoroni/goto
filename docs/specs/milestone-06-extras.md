# Milestone 6: Extras

**Status: Implemented.**

## Scope (from design spec §12)

> Extras: aliases, completion, typo/abbreviation tiers, `--wrong`.

## What this covers from the full spec

- **§4** — CLI contract rows: `--forget <terms>` (delete a remembered decision), `--alias NAME PATH`,
  `--unalias NAME`, `--aliases` (manual bookmarks, used as `goto @name` or `goto name`), `--complete
  WORD` (print tab-completion candidates, one per line).
- **§6** — `aliases.tsv` (`name<TAB>path`) becomes writable here (the resolver's alias *lookup* step
  may already exist since Milestone 2 if the file format was wired up early, but `--alias`/
  `--unalias`/`--aliases` as a management interface are this milestone's).
- **§7, step 3, Tier T4 Abbreviation** — `d*o*c*` wildcard matching, first-char anchored.
- **§5** — tab completion: `Register-ArgumentCompleter -Native -CommandName goto,gt`, calling
  `--complete`; test on both PS 5.1 and 7 since function-based command completion behavior has
  varied between them.
- **§11** — Wrong-guess correction (`goto --wrong`, Phase 6): reopens the picker excluding the last
  result, overwrites the remembered decision; needs a small `last.txt` tracking the last query and
  path. Typo tolerance (Phase 6): when nothing matches, edit-distance ≤ 2 against leaf names already
  present in `visits.tsv` (no filesystem search needed) → "Did you mean `Documents`?". Abbreviations
  (Phase 6): `goto dl` → `Downloads` via the T4 tier. Aliases/bookmarks (Phase 6): as above. Tab
  completion (Phase 6): completes from `visits.tsv` — instant, no search.

Explicitly **not** in scope: Phase 7 items (open-elsewhere, Ctrl+G hotkey, startup-time polish) —
those are Milestone 7.

## Acceptance criteria

- `goto --alias work D:\Clients\Acme` then `goto @work` navigates there; `--unalias work` removes
  it; `--aliases` lists all of them.
- `goto --forget <terms>` deletes the matching row from `decisions.tsv` without touching others.
- `goto dl` resolves to `Downloads` via the abbreviation tier when no exact/prefix/contains match
  exists.
- A mistyped term with no matches suggests a close leaf name from `visits.tsv` by edit distance.
- `goto --wrong` reopens the picker excluding the previous result and overwrites the remembered
  decision for that query.
- Tab completion via `Register-ArgumentCompleter` works for both `goto` and `gt` in PowerShell 5.1
  and 7, sourced from `visits.tsv`.

## Implementation notes (decisions made beyond what the spec states)

- **Critical bug caught before it could ship**: `EverythingQueryBuilder.Build` previously threw for
  `MatchMode.Abbreviation`, and an existing test asserted exactly that. Once T4 became a real tier,
  every `goto` reaching it against a live Everything backend would have crashed. Fixed by building
  the `d*o*c*`-style wildcard query (`folder: wfn:"d*o*c*"`) and replacing the now-false test.
- **ALIAS lookup (step 1) doesn't existence-check or auto-clean stale entries**, unlike the decision
  cache — it's a manual, user-owned bookmark, not goto's opportunistic cache. Fires unconditionally
  under `--list`/`--pick`, gated to single-term queries only (same restriction as the path shortcut),
  and rejects tokens containing `/`/`\` as clearly-multi-term shorthand rather than an alias name.
- **Tab completion is unconditional** (always appended in `RunInit`/`RunInstall`), unlike the prompt
  hook which stays opt-in via `--with-prompt-hook` — completion only runs on Tab press, so there's no
  recurring cost to gate behind a flag. The base `Script` constant remains byte-identical to spec.
- **Found via manual testing, not by inspection: PowerShell silently drops a literal empty-string
  argument when invoking a native exe, in any position** (not just trailing — an earlier "fix"
  involving a trailing `--` sentinel was a false positive from misreading a different test). This
  directly breaks tab completion's most common case (pressing Tab right after `goto ` with nothing
  typed, where `$wordToComplete` is `""`). Fixed by adding a single-token `--complete=WORD` form to
  `ArgParser` (the whole token is never empty, even when `WORD` is) and having the real completer
  script use `"--complete=$wordToComplete"` instead of two separate arguments. The original
  two-argument `--complete WORD` form still works for direct/manual invocation.
- **`RecordChoice` became the single write path** for decision + visit + `last.txt`, replacing three
  separate inline `DecisionStore.Upsert`/`VisitStore.Increment` pairs in `Resolver.Resolve` — both
  the cleanup the new `last.txt` write needed and a correctness improvement (one path instead of four
  that could have drifted out of sync).
- **Typo tolerance only triggers when NO tier matched at all** (`verified.Length == 0`), not when a
  tier matched something that turned out to be a stale/phantom candidate — suggesting a spelling
  alternative in the latter case would be a non-sequitur since the typed term already matched
  something real. Also found during testing: a plain single-character-deletion typo very often
  *still* matches via the new T4 Abbreviation tier (since removing letters preserves subsequence
  order), so it never reaches the did-you-mean path at all — only typos that break the in-order
  subsequence property (e.g. an adjacent-character transposition) genuinely exercise it.
- **`--wrong` auto-resolves a single remaining candidate** rather than always reopening the picker,
  reusing `--pick`'s existing precedent — forcing a picker over one row would be a pointless step the
  codebase already decided against elsewhere.
- **`--wrong` with no/stale `last.txt`** → clean stderr message, exit 1, never a hard error. ALIAS and
  path-shortcut hits deliberately never write `last.txt` (no real candidate set to exclude from).

## Key files

- `Goto/Store/AliasStore.cs`, `Goto/Store/LastResolutionStore.cs` (new)
- `Goto/Core/EditDistance.cs` (new)
- `Goto/Core/Matcher.cs` (T4 `IsAbbreviation`), `Goto/Search/EverythingQueryBuilder.cs` (Abbreviation
  query + required fix)
- `Goto/Core/Resolver.cs` (ALIAS step, `excludePath`, `DidYouMean`, `RecordChoice` consolidation)
- `Goto/Cli/ArgParser.cs` (`--forget`, `--alias`/`--unalias`/`--aliases`, `--complete`/`--complete=`,
  `--wrong`)
- `Goto/Program.cs` (`RunForget`, `RunAlias`, `RunUnalias`, `RunAliasesList`, `RunComplete`,
  `RunWrong`, `DispatchResolveResult` extraction)
- `Goto/Shell/PowerShellInit.cs` (`TabCompletionBlock`)
- `Goto.Tests/Store/AliasStoreTests.cs`, `LastResolutionStoreTests.cs`, `Goto.Tests/Core/
  EditDistanceTests.cs`, extended `MatcherTests.cs`/`ResolverTests.cs`/`ArgParserTests.cs`/
  `EverythingQueryBuilderTests.cs`
