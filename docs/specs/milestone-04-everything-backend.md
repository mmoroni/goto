# Milestone 4: Everything backend

**Status: Implemented.** **Partially superseded:** the auto-mode silent fallback to `WalkSearcher`
and `BackendSelector`'s `BackendMode`/forced-mode branching described below were removed post-
Milestone-7 — see `docs/specs/addendum-04-remove-walker-fallback.md`. Everything is now the only
backend; `--doctor`'s single-`TryConnect`-call precedent documented here is still current.

## Scope (from design spec §12)

> Everything backend: embed DLL, auto-start, fallback, `--doctor`, suggestions for narrowing.

## What this covers from the full spec

- **§1B** — the reasoning for why this backend talks to the Everything *client* over SDK IPC rather
  than reading the MFT/USN journal directly (admin/UAC prompts, multi-second re-enumeration, "the
  cache you wanted to avoid") or building on the service's undocumented pipe protocol. Target SDK
  v2 (Everything 1.4, compatible with 1.5; SDK v3/named pipes and Everything 1.5 are still
  considered alpha).
- **§8, §8a in full** — `EverythingSearcher`: connection flow (IPC error detection, locate
  `Everything.exe` via config → running process → `%ProgramFiles%\Everything*\`, start with
  `-startup`, poll ~2s for IPC + `Everything_IsDBLoaded()`, fall back to the walker on failure),
  query-string construction per mode (Exact/Prefix/Contains + qualifier `path:` pre-filter),
  quoting/escaping rules for user terms (strip embedded `"`, wrap in quotes, preserve `*`/`?`
  wildcards), the exact P/Invoke signatures against `Everything64.dll`, and the embed-as-resource +
  extract-to-`%LOCALAPPDATA%\goto\native\` + hash-check + `NativeLibrary.Load` strategy for staying
  a single exe. Validate every query by typing it into Everything's own search box first.
- **§6** — `config.txt`'s `backend` (`auto | everything | walk`) and `everything_path` keys become
  load-bearing now; backend selection logic from §8 ("try Everything, and if `TryConnect` fails, use
  the walker... print a one-line hint on stderr, rate-limited").
- **§11** — "Be more specific" suggestions (Phase 4): when there are too many matches, compute which
  ancestor folder names split the candidate set and print ready-to-run commands
  (`try: goto alice documents · goto work documents`). `--doctor` (Phase 4): report whether
  Everything is reachable, which backend is active, whether the data dir is writable.
- **§4** — the `--doctor` CLI contract row.
- **§13** — acceptance test 7 (Everything not running → fallback with a one-line stderr hint).

Explicitly **not** in scope: ranking/frecency (§9, Milestone 5) — Everything just becomes a second,
faster source of raw candidates feeding the same Milestone 2/3 resolver and picker pipeline.

## Acceptance criteria

- With Everything running and its folders indexed, `goto` queries resolve via the SDK, verified by
  typing the equivalent query into Everything's own search box and confirming the same folders.
- With Everything not running but installed, `goto` auto-starts it in the background (`-startup`),
  polls for DB load, and proceeds — or falls back to the walker with a rate-limited stderr hint if
  it still can't connect within the poll window.
- With Everything entirely absent, behavior is identical to Milestone 2 (silent walker fallback,
  one-line hint).
- `Everything64.dll` is embedded in the single exe, not shipped as a separate file; first run
  extracts it to `%LOCALAPPDATA%\goto\native\` and re-extracts only if its hash changed.
- `goto --doctor` reports Everything reachability, active backend, and data-dir writability.
- "Too many matches" output includes concrete suggested narrower commands, not just a raw list.

## Implementation notes (decisions made beyond what the spec states)

- **Everything SDK sourced and verified empirically, not guessed.** Downloaded
  `https://www.voidtools.com/Everything-SDK.zip`, extracted `Everything64.dll` to
  `Goto/Search/Native/Everything64.dll` (committed, embedded as a resource), and cross-checked every
  P/Invoke constant against the real `Everything.h`. All three (`EVERYTHING_OK = 0`,
  `EVERYTHING_ERROR_IPC = 2`, `EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME = 0x00000004`) matched my
  pre-verification guesses exactly — but **`Everything_SetSearchW` returns `void`, not `uint`** as
  I'd initially assumed; the header caught this before it became a runtime bug. `-startup` was
  confirmed correct against voidtools' command-line options page ("runs in the background without
  search windows"). License (MIT-style, redistribution permitted with notice) saved verbatim to
  `docs/third-party/Everything-SDK-LICENSE.txt`.
- **`NativeLibrary.SetDllImportResolver` works correctly under Native AOT** — confirmed by an actual
  `build.ps1` + exe run against the real Everything client, not just a successful compile. No
  fallback (DLL-next-to-exe) was needed.
- **Forced `backend = everything` but unreachable → hard error, exit 3** (`ResolveKind.BackendError`,
  reusing `ResolveResult.Path` to carry the message — no new field needed), not a silent walker
  substitution. Verified manually: stopping Everything and forcing `backend = everything` correctly
  produced exit 3 with a clear message, not a silent fallback.
- **`--doctor` must call `EverythingSearcher.TryConnect` exactly once**, not once directly plus again
  inside `BackendSelector.Select` — caught via manual testing: the first call's auto-start attempt
  can still be loading Everything's DB when a second, independent `TryConnect` call starts polling
  from zero, risking a duplicate `Process.Start(-startup)`. Fixed by deriving the "active backend"
  line from the single `TryConnect` result via a small `config.Backend switch`, rather than calling
  `BackendSelector.Select` a second time. `--doctor` writes to stdout via plain `Console.WriteLine`
  (not `RawOutput`) since it's a meta-command that never participates in path resolution.
- **"Be more specific" suggestions capped at 3**, ordered by how evenly they'd split the candidate
  set, then frequency, then alphabetically — shares `Matcher.GetAncestorNames` (now `internal`, was
  `private`) with the qualifier-matching logic so both stay in sync. `ResolveResult` gained
  `LeafTerm` (populated only for `TooMany`) so `Program.cs` doesn't need a second
  `GotoQuery.FromPositionals` call to get it.
- **Everything-unavailable hint is rate-limited to once per 30 minutes** via a plain timestamp file
  (`last-everything-hint.txt` in the data dir) — no mutex, since a lost race just means the hint
  prints twice, harmless unlike decision/visit data.
- **Manual verification of the true end-to-end "Everything absent, walker takes over silently with
  hint" path was not fully isolated** — doing so reliably would require making the real, properly
  installed Everything.exe on this dev machine completely unfindable (not just stopped), which is
  too invasive for a manual check. Verified the individual pieces instead: the forced-`everything`
  hard-error path (which exercises the identical `TryConnect` failure branch), the rate-limit
  timestamp logic (read via code review — simple enough not to warrant its own test), and `auto`
  mode's fallback conditional (a 2-line branch reusing the same proven `TryConnect` call).
- **Non-ASCII round-trip confirmed correct** (a `Café` folder resolved correctly via Everything,
  case-insensitively even) — an initial "no match" during testing was purely USN-journal indexing
  latency for a just-created folder, not an encoding bug; a longer wait resolved it.
- Two gotchas repeated from earlier milestones, worth restating since this milestone's manual testing
  tripped on them again: (1) test trees under `%LOCALAPPDATA%\Temp` need an explicit non-default
  `exclude` value in `config.txt`, or the walker's default `AppData\Local\Temp` exclusion prunes the
  test fixture itself; (2) `walk_roots` only scopes the **walker** — the Everything backend ignores
  it entirely (it queries Everything's whole index), so a `backend = auto`/`everything` test that
  expects root-scoped results will instead see real, whole-filesystem matches if Everything is (or
  becomes, via auto-start) reachable.

## Key files

- `Goto/Search/Native/Everything64.dll` (embedded resource, MIT-licensed, see
  `docs/third-party/Everything-SDK-LICENSE.txt`)
- `Goto/Search/EverythingNative.cs`, `NativeLibraryExtractor.cs`, `EverythingSearcher.cs`,
  `EverythingQueryBuilder.cs`, `BackendSelector.cs`, `EverythingFallbackHint.cs`
- `Goto/Core/Suggestions.cs`
- `Goto/Store/Config.cs` (`backend`, `everything_path`)
- `Goto/Core/Resolver.cs` (`BackendError`, `LeafTerm`), `Goto/Program.cs` (`RunDoctor`, suggestions)
- `Goto.Tests/Search/EverythingQueryBuilderTests.cs`, `BackendSelectorTests.cs`,
  `Goto.Tests/Core/SuggestionsTests.cs`, extended `ConfigTests.cs`/`ArgParserTests.cs`
