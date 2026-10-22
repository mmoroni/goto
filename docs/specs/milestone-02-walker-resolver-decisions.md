# Milestone 2: Walker + resolver + decisions

**Status: Implemented.**

## Scope (from design spec §12)

> Walker + resolver + decisions: tiers, qualifiers, the decision file, stale-entry cleanup.
> Done when `goto documents` works with no Everything and the second run does no search.

## What this covers from the full spec

- **§4** — the general-case CLI contract row: `goto <term> [<term>…]` where the last term is the
  leaf and earlier terms are ancestor qualifiers. This is also where the §4 rule "backslashes and
  slashes inside an argument split it into terms" actually gets implemented (deferred from M1).
- **§6** — on-disk files: `decisions.tsv` format and the general writing rules (named mutex
  `Local\goto-store`, write-to-`.tmp`-then-atomic-rename, canonical paths via `Path.GetFullPath`,
  `OrdinalIgnoreCase` comparison). `visits.tsv`/`aliases.tsv`/`config.txt` can be introduced now too
  since `AtomicFile`/mutex plumbing is shared, but `config.txt`'s `backend`/`everything_*` keys and
  `visits.tsv`'s use for ranking aren't load-bearing until Milestones 4–5.
- **§7** — resolution algorithm steps 1–2 (alias exact-match passthrough if `aliases.tsv` exists;
  decision lookup with stale-entry cleanup) and step 3 tiers T1–T3 (Exact/Prefix/Contains — T4
  Abbreviation is explicitly Phase 6). Step 4 (no tier matched → exit 1). Step 6 (upsert
  decision, increment visit) — note ranking itself (step 5, §9) is Milestone 5; with only one
  backend and no ranking yet, "first verified match" or "single match" resolution suffices here.
  **Qualifier matching** sub-algorithm (leaf vs. ancestor checked separately, greedy forward scan)
  is fully in scope.
- **§8, §8b** — `IFolderSearcher` interface and `WalkSearcher` (the fallback — and, until Milestone
  4, the *only* backend): explicit `Stack<string>` traversal (no recursion), roots in order (cwd
  subtree → `%USERPROFILE%` → fixed drives), `EnumerationOptions` skipping reparse points/system
  files, config `exclude` list applied while walking, `Parallel.ForEach` over first-level
  subfolders, `walk_timeout_ms` cancellation with partial-results message.
- **§13** — acceptance tests 1, 2, 4, 5, 6 directly target this milestone (single exact match;
  contains-tier fallback; qualifier filtering; decision-file short-circuit on second run; stale
  decision cleanup). Edge cases: paths over 260 chars, network/UNC drives skipped by the walker,
  two terminals writing at once, corrupt/hand-edited data files (skip bad lines, never crash),
  first run with no data dir, access-denied folders ignored by the walker.

Explicitly **not** in scope: the Everything backend (§8a), the picker and "too many" interactive
flow (§10 — M2 can resolve ambiguity by simplest-available rule, e.g. first match or exit 2 with a
plain list, since the real picker is Milestone 3), ranking/frecency scoring (§9, Milestone 5),
`--explain` (Milestone 5), aliases as a *writable* feature via `--alias`/`--unalias` (Milestone 6 —
though the alias *lookup* step in the resolver can exist now if the file format is already defined
in §6).

## Acceptance criteria

- `goto documents` resolves correctly with no Everything running, using only the walker.
- Running the same query twice: the second run hits the decision file and performs **no** search.
- Deleting the resolved folder on disk causes the next run to detect the stale decision, delete it,
  and re-search.
- `goto user documents` only matches `documents` folders with `user` somewhere in an ancestor name.

## Implementation notes (decisions made beyond what the spec states)

- **No alias lookup in M2.** `aliases.tsv` has no writer until Milestone 6 (`--alias`/`--unalias`).
  An always-empty file checked on every resolve would be dead code with nothing to verify — so
  alias lookup *and* storage both land together in M6. `Resolver`'s step 1 (ALIAS) is simply absent.
- **Ambiguous-match fallback: exit 2 + stderr candidate list (capped at 20, alphabetical), not
  silently picking one.** M2 has no picker (Milestone 3), so rather than guess which of several
  tied candidates the user meant, `Resolver.Resolve` returns `ResolveKind.TooMany` and `Program.cs`
  prints them and exits 2 (`AmbiguousOrCancelled`). Safer than a silent `cd`, and closer to M3's
  eventual behavior.
- **`WalkSearcher` root/parallelism algorithm** (spec §8b states it in one line): for each candidate
  root in order (`config.WalkRoots` if set, else cwd → `%USERPROFILE%` → each fixed drive), enumerate
  its first-level subfolders, `Parallel.ForEach` over them with an explicit `Stack<string>` DFS per
  branch (never recursion), checking each directory's name against the leaf/mode as it's visited and
  pruning excluded subtrees before pushing their children onto the stack. Stop entirely at the first
  root that yields any raw match — don't walk subsequent roots. The shared `CancellationToken` (from
  `config.WalkTimeoutMs`) covers the whole call, not per-root; a cancellation sets
  `WalkSearcher.LastSearchWasIncomplete`, which `Resolver` surfaces as a stderr hint.
- **Both `Resolver.Resolve` and `WalkSearcher`'s first-level enumeration apply `config.IsExcluded`**
  — not just the deeper stack traversal — so a literally-excluded top-level folder (e.g. a
  `node_modules` sitting directly under a walk root) is pruned too, not just its descendants.
- **Testability seams**: `Resolver.Resolve(args, searcherOverride, configOverride)` takes optional
  `IFolderSearcher`/`Config` overrides (both default to the real `WalkSearcher`/`Config.Load()`)
  specifically so tests can inject a fake searcher and a config with no `exclude` entries — needed
  because `Directory.CreateTempSubdirectory()` places test fixtures under `%LOCALAPPDATA%\Temp`,
  which the real default exclude list (`...;AppData\Local\Temp`) would otherwise prune. This was
  discovered the hard way: several tests and a manual verification pass initially failed because the
  walker correctly pruned the test trees themselves. The real default exclude list is working as
  intended for actual use — it's a test-environment-only concern.
- **`decisions.tsv` timestamp format**: `yyyy-MM-ddTHH:mm:ss'Z'`, matching the spec's literal example
  (no fractional seconds, trailing literal `Z`) rather than `DateTime`'s `"O"` round-trip format.
- **`GOTO_HOME` test isolation**: `DataPaths.ResolveDataDir()` reads the env var fresh on every call
  (never cached), and `Goto.Tests` disables test-collection parallelization assembly-wide
  (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) since tests mutate this
  process-wide env var per test rather than threading a data-dir parameter through every store.
- **Qualifier ancestor-matching walks the full real ancestor chain**, not just ancestors within the
  configured walk root — this matches the spec's own worked example (`Users` qualifies
  `C:\Users\alice\Documents` even though `Users` is near the drive root, well above wherever a walk
  might have started). One consequence: on a real machine, a qualifier that happens to match a
  segment of the ambient path (e.g. `"user"` matching the literal `C:\Users\<you>\...` every file
  lives under) will match far more broadly than intended — this is spec-faithful behavior, not a bug,
  but worth knowing when writing qualifier-matching tests or filing "irrelevant matches" reports.
- `Goto.sln` is actually `goto.slnx` — .NET 10's `dotnet new sln` now defaults to the `.slnx` format.

## Key files

- `Goto/Core/GotoQuery.cs`, `Goto/Core/Matcher.cs`, `Goto/Core/Resolver.cs` (rewritten)
- `Goto/Search/IFolderSearcher.cs`, `Goto/Search/WalkSearcher.cs`
- `Goto/Store/DataPaths.cs`, `Goto/Store/AtomicFile.cs`, `Goto/Store/Config.cs`,
  `Goto/Store/DecisionStore.cs`, `Goto/Store/VisitStore.cs`
- `Goto.Tests/` (new xUnit project — `Core/MatcherTests.cs`, `Core/ResolverTests.cs`,
  `Search/WalkSearcherTests.cs`, `Fakes/FakeFolderSearcher.cs`, `Fakes/TempTree.cs`,
  `Fakes/TempGotoHome.cs`)
