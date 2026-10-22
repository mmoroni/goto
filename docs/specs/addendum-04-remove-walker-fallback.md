# Addendum 4: Remove the filesystem-walk fallback

**Status: Implemented.**

Like the other addenda, not called for by the original design spec — in fact the opposite: §8b of
`docs/specs/00-design-spec.md` explicitly calls for a filesystem-walk fallback when Everything isn't
available, and Milestone 2 built it, Milestone 4 wired `auto` mode to silently fall back to it. This
addendum removes that fallback entirely, on direct user request, after hitting it in practice:
> "the fallback is unbearable slow. It's slow and it locks the input, as I could not Ctrl-C out of
> it."

## Why remove it rather than fix it

Two options were on the table: (a) keep `WalkSearcher` as an explicit, documented opt-in
(`backend = walk`) for machines without Everything, just stop the *silent* auto-fallback when
Everything is unreachable in `auto` mode; or (b) delete the walker outright, making Everything a
hard requirement. The user chose (b). Fixing the walker to be properly cancellable (so Ctrl+C
actually works mid-scan) would have been meaningfully more engineering than either of the above —
the reported "can't Ctrl+C out of it" symptom traces to `Console.ReadKey`/the picker's input loop
never running while `WalkSearcher.FindFolders` blocks synchronously inside `Resolver.Resolve`, and
`Parallel.ForEach`'s cancellation-token checks only happen between directory visits, not inside a
single slow `Directory.EnumerateDirectories` call on a huge folder — fixing that properly would need
a redesign of the walk loop's cancellation granularity, not a quick patch. Removing the whole
feature was simpler, more honest about what the tool actually depends on, and directly eliminates
the bug class rather than papering over it.

## What changed

- **Deleted** `Goto/Search/WalkSearcher.cs` and `Goto/Search/EverythingFallbackHint.cs` (the
  rate-limited "falling back to the walker" stderr hint — nothing left to hint about).
- **`Goto/Search/BackendSelector.cs`** simplified to its essential shape: try to connect to
  Everything, return it if reachable, return a hard error (`BackendError`, exit 3) if not. No more
  `BackendMode` branching.
- **`Goto/Store/Config.cs`**: removed the `BackendMode` enum, the `Backend` property, `WalkRoots`,
  and the `backend`/`walk_roots` config-file keys entirely — with only one backend left, a `backend`
  setting has nothing to select between. **Renamed** `WalkTimeoutMs`/`walk_timeout_ms` to
  `SearchTimeoutMs`/`search_timeout_ms`: this field was never actually walker-specific — it feeds
  the `CancellationTokenSource` that bounds *every* backend's search call in `Resolver.Resolve`
  (`Goto/Core/Resolver.cs`), Everything included. Keeping the old name after the walker's removal
  would have been actively misleading about what it does. `Exclude`/`IsExcluded` stays exactly as
  it was — it was never walker-specific either; `Resolver.Resolve` applies it to every backend's raw
  results (line ~117), not just inside the old `WalkSearcher`.
- **`Goto/Core/Resolver.cs`**: removed the `searcher is WalkSearcher { LastSearchWasIncomplete }`
  stderr-hint check (nothing produces partial/incomplete results anymore — Everything's query is a
  single synchronous round trip, not an interruptible scan).
- **`Goto/Program.cs`**: `RunDoctor` no longer reports an "active backend" line computed from
  `BackendMode` — Everything reachability *is* the whole diagnostic now. Unreachable gets a more
  actionable message: `Everything: NOT reachable (...) -- gt will fail until Everything is running`.
- **`Goto/Cli/HelpText.cs`**: `--doctor`'s description and the exit-code line updated to drop
  "which search backend is active" (nothing to distinguish anymore) and spell out that Everything
  is a hard requirement.
- Deleted `Goto.Tests/Search/WalkSearcherTests.cs` and `Goto.Tests/Search/BackendSelectorTests.cs`
  (the latter's only test asserted `backend = walk` behavior, which no longer exists — no
  replacement test was added, consistent with Milestone 4's own precedent that Everything-reachable/
  unreachable behavior is manual-only, not unit-tested, since `EverythingSearcher` has no fake/mock
  seam to force a connection failure in a test). `Goto.Tests/Store/ConfigTests.cs`'s
  backend-mode tests were replaced with `search_timeout_ms` parsing tests.
- `README.md` (root) and `CLAUDE.md` updated to state Everything is a hard requirement, not an
  optional fast path with a fallback.

## Acceptance criteria

- `gt <term>` with Everything not running fails fast (near-instant, not a multi-second scan) with a
  clear stderr message and exit 3.
- No code path in the program ever walks the filesystem directly looking for a match.
- `gt --doctor` reports Everything reachability plainly, with no "active backend" concept.
- `search_timeout_ms` (renamed from `walk_timeout_ms`) still bounds every search call, including
  Everything's.

## Key files

- Deleted: `Goto/Search/WalkSearcher.cs`, `Goto/Search/EverythingFallbackHint.cs`,
  `Goto.Tests/Search/WalkSearcherTests.cs`, `Goto.Tests/Search/BackendSelectorTests.cs`
- `Goto/Search/BackendSelector.cs`, `Goto/Store/Config.cs`, `Goto/Core/Resolver.cs`,
  `Goto/Program.cs` (`RunDoctor`), `Goto/Cli/HelpText.cs`
- `Goto.Tests/Store/ConfigTests.cs`
