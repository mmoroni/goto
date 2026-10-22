# `goto` / `gt`: design spec

This is the full original design spec as provided, kept verbatim as the source of truth. Per-milestone
breakdowns (scope, acceptance criteria, implementation notes) live alongside this file as
`milestone-0N-*.md`.

## 1. Two constraints that shape everything

**A. A child process can't change its parent shell's directory.** A standalone `goto.exe` can find the folder, but only the shell can `cd` into it. The standard fix (zoxide and others do this) is:

- `goto.exe` does all the work and prints **only the destination path to stdout**.
- A 6-line PowerShell function named `goto` calls it and runs `Set-Location`.
- The exe can emit that function itself (`goto.exe --init powershell`), so the exe is the only artifact you ship. Installation is one line in `$PROFILE`.

**B. Everything's Windows service doesn't run searches.** The service helps Everything index NTFS volumes and monitor USN journals, it has no interface, and it exposes only filenames, not file access. That's why Everything can run as a standard user. The search happens in the Everything client process, and the SDK is a basic IPC wrapper that requires the Everything client to be running. The service's own pipe protocol is undocumented, so don't build on it.

Your "same system calls" option is possible but a bad fit:

- Everything reads the Master File Table and the USN Journal of each NTFS volume.
- Opening those requires administrator rights, so every `goto` would trigger a UAC prompt.
- Re-enumerating about a million entries on every invocation takes seconds.
- Avoiding both problems means writing your own service and index, which is the cache you wanted to avoid.

**Decision:** talk to the running Everything client through its SDK, and keep a plain filesystem walker as a fallback for when Everything isn't available.

On versions: SDK v2 targets Everything 1.4 and remains compatible with 1.5. SDK v3 (for 1.5) switched to named pipes. Voidtools' forum still labels 1.5 as alpha, so target **SDK v2** for now.

## 2. Architecture

```
PowerShell                         goto.exe (one native exe)
┌─────────────────┐   args    ┌──────────────────────────────────────────────┐
│ function goto   │ ────────▶ │ ArgParser → Resolver                         │
│  (from --init)  │           │  1. path shortcut / alias                    │
│                 │           │  2. remembered decision (text file)          │
│ Set-Location    │ ◀──────── │  3. tiered search via IFolderSearcher        │
│   $dest         │  stdout   │       ├─ EverythingSearcher (SDK over IPC)   │
└─────────────────┘ (path     │       └─ WalkSearcher (fallback)             │
                     only)    │  4. Ranker → auto-pick | Picker | "too many" │
   stderr = all human UI ───▶ └──────────────────────────────────────────────┘
```

**Golden rule: stdout contains only the destination path. Every message, list and picker goes to stderr.** The wrapper relies on this.

## 3. Tech stack and project setup

- **C# on .NET 10 (current LTS; .NET 8 works the same), published as Native AOT, x64.** This gives a single exe of a few MB that starts in about 10–20 ms. That matters because it runs on every command.
- Avoid reflection-heavy features. Don't use JSON serialization (the data files below are plain text), and don't use `StringBuilder` marshalling in P/Invoke (use `char*`).

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

Build with `dotnet publish -c Release`.

Suggested layout:

```
Goto/
  Program.cs                  // entry point, exit codes
  Cli/ArgParser.cs
  Core/GotoQuery.cs  Resolver.cs  Matcher.cs  Ranker.cs  PathShortcut.cs
  Search/IFolderSearcher.cs  EverythingSearcher.cs  EverythingNative.cs  WalkSearcher.cs
  Store/DecisionStore.cs  VisitStore.cs  AliasStore.cs  Config.cs  AtomicFile.cs
  Ui/Picker.cs  Vt.cs  Log.cs                // everything writes to stderr
  Shell/PowerShellInit.cs                    // the script text for --init
Goto.Tests/                                  // xUnit
```

## 4. CLI contract

Each argument is one search term. Terms are case-insensitive. Backslashes and slashes inside an argument split it into terms, so `goto user/documents` equals `goto user documents`.

| Usage | Behavior |
|---|---|
| `goto <term> [<term>…]` | Resolve and print the destination. **The last term is the folder name. Earlier terms are qualifiers** that must appear in ancestor folder names, in order. |
| `goto -` | Handled in the wrapper: go back to the previous directory. |
| `-l, --list` | Show ranked candidates on stderr and don't navigate. |
| `-p, --pick` | Ignore the remembered decision, force the picker, and remember the choice. |
| `--refresh` | Ignore the remembered decision and re-search with normal auto-selection. |
| `--explain` | Show where the result came from and the score breakdown per candidate. |
| `--forget <terms>` | Delete a remembered decision. |
| `--alias NAME PATH`, `--unalias NAME`, `--aliases` | Manual bookmarks, used as `goto @name` or `goto name`. |
| `--add PATH` | Record a visit (used by the prompt hook). Prints nothing. |
| `--complete WORD` | Print tab-completion candidates, one per line. |
| `--init powershell` | Print the wrapper script. |
| `--install` | Idempotently append the init line to `$PROFILE`. |
| `--doctor` | Diagnose: is Everything reachable, is the data dir writable, and so on. |
| `--` | End of options (for folders whose names start with `-`). |

**Exit codes** (the wrapper only navigates on `0`):

| Code | Meaning |
|---|---|
| 0 | Success, path on stdout |
| 1 | No match |
| 2 | Ambiguous or too many matches, or the user cancelled the picker |
| 3 | Backend or config error |
| 4 | Usage error |

## 5. PowerShell integration (emitted by `--init powershell`)

```powershell
$global:GOTO_EXE = (Get-Command goto.exe -CommandType Application -ErrorAction Stop).Source

function goto {
    if ($args.Count -eq 1 -and $args[0] -eq '-') {            # like `cd -` (works on PS 5.1 too)
        if ($global:__gotoPrev) { $t = $PWD.Path; Set-Location -LiteralPath $global:__gotoPrev; $global:__gotoPrev = $t }
        return
    }
    $prevEnc = [Console]::OutputEncoding
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)   # non-ASCII folder names
    try {
        $out  = & $global:GOTO_EXE @args      # capture ALL output, then read the exit code
        $code = $LASTEXITCODE
    } finally { [Console]::OutputEncoding = $prevEnc }
    if ($code -eq 0 -and $out) {
        $global:__gotoPrev = $PWD.Path
        Set-Location -LiteralPath (@($out)[0])
    }
}
Set-Alias gt goto
```

Gotchas you must respect:

- **Don't pipe to `Select-Object -First 1` before reading `$LASTEXITCODE`.** It can stop the pipeline early and leave a stale exit code.
- **The exe must write its stdout as raw UTF-8 bytes** (`Console.OpenStandardOutput()`), not through `Console.Out`. The wrapper switches the console to UTF-8 only while the exe runs.
- **Execution policy:** Windows PowerShell 5.1 blocks profile scripts by default. The user may need `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`. `--install` should print this hint.

**Optional (Phase 5): learn from plain `cd`.** `--init` can also wrap `prompt` so that when `$PWD` differs from the last-seen value, it runs `& $global:GOTO_EXE --add $PWD.Path`. This means directories you reach without `goto` still build your frecency. Save and restore `$LASTEXITCODE` inside the hook so you don't corrupt the user's variable.

**Optional: tab completion.** Use `Register-ArgumentCompleter -Native -CommandName goto,gt` and have the script block call `--complete`. Test it on both PS 5.1 and 7, since behavior with function-based commands has varied.

## 6. Files on disk (the "memory," not an index cache)

Location: `%LOCALAPPDATA%\goto\`, overridable with the `GOTO_HOME` env var. If a `goto-data` folder sits next to the exe, use it instead (portable mode).

All files are UTF-8, one record per line, tab-separated, with `#` comment lines ignored. Windows paths can't contain tabs, so TSV is safe.

`decisions.tsv`: what you asked for → where you ended up
```
# query <TAB> path <TAB> lastUsedUtc
documents    C:\Users\alice\Documents    2026-10-22T14:03:11Z
user documents    C:\Users\alice\Documents    2026-10-20T09:12:45Z
```
The query key is the terms lowercased and joined by a single space.

`visits.tsv`: frecency data, one row per visited folder
```
# path <TAB> visitCount <TAB> lastVisitUtc
C:\Users\alice\Documents    42    2026-10-22T14:03:11Z
```

`aliases.tsv`: `name<TAB>path`

`config.txt`: simple `key = value` lines, with lists separated by `;`
```
backend = auto               # auto | everything | walk
everything_path =            # blank = auto-detect
max_candidates = 15
dominance_margin = 15
exclude = node_modules;$Recycle.Bin;System Volume Information;WinSxS;.git;AppData\Local\Temp
walk_roots =                 # blank = cwd, user profile, then all fixed drives
walk_timeout_ms = 3000
```

**Writing rules:**

- Wrap every read-modify-write in a named mutex (`Local\goto-store`).
- Write to `file.tmp`, then `File.Move(tmp, file, overwrite: true)`. Two terminals can run `goto` at once, and this keeps the files from corrupting.
- Store canonical paths: `Path.GetFullPath`, with the trailing `\` trimmed except for drive roots.
- Compare with `StringComparison.OrdinalIgnoreCase` everywhere. Keep the filesystem's casing for display.

## 7. Resolution algorithm

```
Resolve(query):
  0. PATH SHORTCUT: if there is one term and it is an existing path (".", "..", "~", "C:\x",
     relative dir) → print it. This keeps goto usable as a drop-in cd.
  1. ALIAS: "@name" or an exact alias match → print.
  2. DECISION: decisions[key] exists?
        folder still exists → print (done; no search at all)
        folder is gone      → delete the entry, continue
  3. TIERS (stop at the first tier with ≥ 1 verified match):
        T1 Exact       leaf == term
        T2 Prefix      leaf starts with term
        T3 Contains    leaf contains term
        T4 Abbrev      d*o*c*  (first char anchored)       [Phase 6]
     For each tier:
        raw     = searcher.FindFolders(...)                // backend may return a superset
        matches = raw.Where(Matcher.IsMatch)               // resolver re-verifies: it is the source of truth
                     .Where(not excluded)
  4. No tier matched → exit 1 with "No folder matching 'x'" + "did you mean" (see section 11).
  5. Rank matches (section 9), then decide:
        1 match                              → go
        top score − 2nd score ≥ margin       → go ("dominant winner")
        count ≤ max_candidates               → interactive picker
        else                                 → "Too many (N). Be more specific." + suggestions, exit 2
  6. On success: upsert decision(key → path), increment visits(path).
```

**Qualifier matching (`Matcher.IsMatch`):** given `goto user documents`:

1. The leaf folder name must satisfy the tier rule against `documents`.
2. Each qualifier must substring-match an ancestor folder name, in left-to-right order, using a greedy forward scan.
3. `C:\Users\alice\Documents` passes, because `Users` contains `user`.

Check the leaf and the ancestors separately, so a qualifier is never satisfied by the leaf itself.

**Non-interactive safety:** if `Console.IsInputRedirected` is true or `--no-ui` is passed, never show the picker. Print the candidates to stderr and exit 2.

## 8. Search backends

```csharp
public enum MatchMode { Exact, Prefix, Contains, Abbreviation }

public interface IFolderSearcher
{
    string Name { get; }
    bool TryConnect(out string? whyNot);     // cheap check; no heavy work
    IReadOnlyList<string> FindFolders(
        string leaf, IReadOnlyList<string> qualifiers,
        MatchMode mode, int limit, CancellationToken ct);
}
```

Backend selection (`backend = auto`): try Everything, and if `TryConnect` fails, use the walker. If the walker was used, print a one-line hint on stderr, rate-limited.

### 8a. EverythingSearcher

**Connection:**

1. Is Everything running? The SDK reports an IPC error (`EVERYTHING_ERROR_IPC`) when it isn't.
2. If not, find `Everything.exe` (config value, then the running process, then `%ProgramFiles%\Everything*\`).
3. Start it in the background with `-startup`. Verify the flag against your version's command-line docs.
4. Poll about 2 seconds for IPC and for `Everything_IsDBLoaded()`.
5. If it still fails, fall back to the walker.

Recommend the user enable "Run in background" and "Start Everything on system startup" in Everything's options, since wording varies by version.

**Query strings.** These come from Everything's search syntax: `folder:` matches folders only, `wfn:` matches the whole filename, and `startwith:<text>` matches filename prefixes. A space means AND, and the `path:` modifier matches text anywhere in the full path.

| Mode | Query sent |
|---|---|
| Exact | `folder: wfn:"documents"` |
| Prefix | `folder: startwith:"docu"` |
| Contains | `folder: "docu"` |
| Qualifiers (any mode) | append `path:"user"` for each one (a coarse pre-filter; the Resolver re-checks properly) |

**Always validate a query by typing it into Everything's own search box first.** If the GUI shows the right folders, the SDK will return the same ones.

- Wrap user terms in double quotes and strip embedded `"` characters. Otherwise spaces, `|`, `<`, `>` and `!` would be parsed as Everything operators.
- `*` and `?` stay as wildcards, which is a handy feature.

**SDK calls** (`Everything64.dll` from the SDK zip, v2). Copy the exact constants from the SDK's `Everything.h`:

```csharp
internal static class EverythingNative
{
    const string Dll = "Everything64.dll";
    [DllImport(Dll, CharSet = CharSet.Unicode)] internal static extern uint Everything_SetSearchW(string q);
    [DllImport(Dll)] internal static extern void Everything_SetRequestFlags(uint flags);   // FULL_PATH_AND_FILE_NAME
    [DllImport(Dll)] internal static extern void Everything_SetMax(uint max);
    [DllImport(Dll)] internal static extern bool Everything_QueryW(bool wait);             // true = blocking
    [DllImport(Dll)] internal static extern uint Everything_GetNumResults();
    [DllImport(Dll, EntryPoint = "Everything_GetResultFullPathNameW")]
    internal static extern unsafe uint GetFullPath(uint index, char* buf, uint size);
    [DllImport(Dll)] internal static extern uint Everything_GetLastError();
    [DllImport(Dll)] internal static extern bool Everything_IsDBLoaded();
}
```

Flow: `SetSearchW` → `SetRequestFlags` → `SetMax(2000)` → `QueryW(true)` → loop over `GetNumResults()` reading each path.

**Staying a single exe:** embed `Everything64.dll` as an `EmbeddedResource`. On first run, extract it to `%LOCALAPPDATA%\goto\native\` (re-extract if the hash differs) and load it with `NativeLibrary.Load`. Read the SDK's license before redistributing the DLL. A later option is to speak the WM_COPYDATA protocol directly, which removes the DLL entirely. The layout is in the SDK's `everything_ipc.h`, but it needs a hidden message window and is harder for a first version.

**Prerequisites to document:**

- Everything must be running.
- The folders you want must be indexed. NTFS fixed drives are by default; network and ReFS drives may need manual setup.

### 8b. WalkSearcher (fallback, no Everything needed)

- **Roots, in order:** the current directory's subtree, then `%USERPROFILE%`, then each `DriveType.Fixed` drive. Stop at the first tier that finds something.
- **Traversal:** an explicit `Stack<string>` (never recursion; deep trees overflow), using `Directory.EnumerateDirectories` with `EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = ReparsePoint | System }`. Skipping reparse points avoids junction loops like `Application Data`.
- **Parallelism:** one `Parallel.ForEach` over each root's first-level subfolders.
- **Excludes:** apply the config `exclude` list while walking so excluded subtrees are pruned, not just filtered.
- **Time budget:** `CancellationTokenSource(walk_timeout_ms)`. On timeout, return partial results and print "(search incomplete)" on stderr.

## 9. Ranking

Ranking is only needed once there are multiple matches within a tier. These are **starting weights; expose them via `--explain` and tune them**.

```
score = 10 * log2(1 + visits * recencyFactor)        // frecency, 0 if never visited
      + 30 if path is under cwd
      + 15 elif path is under cwd's parent           // sibling projects
      + 15 if path is under %USERPROFILE%
      + 10 if folder looks like a project root       // .git, *.sln, package.json… (check top 20 only)
      -  1.5 * depth                                  // shallower is better
```

`recencyFactor`: ×4 if visited within the last hour, ×2 within a day, ×0.5 within a week, ×0.25 older. This is zoxide's scheme.

Aging: when the sum of all visit counts exceeds about 10,000, multiply every count by 0.9 and drop entries below 1.

**Existence check:** `Directory.Exists` only on the top ~50 ranked candidates (Everything's index can briefly include deleted folders). Don't stat 2,000 paths.

## 10. Picker UI (`Ui/Picker.cs`)

- Draw to **stderr** with ANSI escapes. Enable VT on the handle with `GetConsoleMode`/`SetConsoleMode` (`ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004`), because older conhost needs it.
- Read keys with `Console.ReadKey(intercept: true)`. This works even though stdout is captured, since stdin is still the console.
- Keys: ↑/↓ move, Enter select, Esc/Ctrl+C cancel (exit 2), 1–9 jump to that entry, and typing filters the list.
- Each row shows the full path with the matched leaf highlighted, plus a dim annotation like `last used 2d ago · git repo`.
- Redraw in place by moving the cursor up N lines. Restore the cursor on exit, including on exceptions (`try/finally`).

## 11. Ideas to make `goto` smarter

| Idea | What it does | Phase |
|---|---|---|
| **Frecency learning** | Every successful `goto` (and, via the prompt hook, every plain `cd`) feeds ranking. | 5 |
| **Context awareness** | Candidates under or near your current directory outrank distant ones. `goto src` inside a project goes to *that* project's `src`. | 5 |
| **"Be more specific" suggestions** | When there are too many matches, compute which ancestor folder names split the set (appear in some candidates but not all) and print ready-to-run commands: `try: goto alice documents · goto work documents`. | 4 |
| **`--explain`** | Shows why a path won (source, tier, each score term). Essential for tuning and bug reports. | 5 |
| **Wrong-guess correction** | `goto --wrong` reopens the picker, excluding the last result, and overwrites the remembered decision. Needs a small `last.txt` with the last query and path. | 6 |
| **Typo tolerance** | If nothing matches, compare the term against leaf names in `visits.tsv` using edit distance ≤ 2 and ask "Did you mean `Documents`?" Needs no filesystem search. | 6 |
| **Abbreviations** | `goto dl` → Downloads, using the wildcard tier (T4). | 6 |
| **Project awareness** | Prefer repo roots (`.git`, `.sln`, `package.json`, `Cargo.toml`). | 5 |
| **Aliases/bookmarks** | `goto --alias work D:\Clients\Acme`, then `goto @work`. | 6 |
| **`goto -`** | Previous directory (already in the wrapper). | 1 |
| **Tab completion** | Completes from `visits.tsv` (instant, no search). | 6 |
| **Open elsewhere** | `goto -e docs` opens in Explorer, `goto -c docs` in VS Code. | 7 |
| **`Ctrl+G` hotkey** | A PSReadLine key handler that opens the picker over everything you've visited. | 7 |
| **`--doctor`** | Prints whether Everything is reachable, which backend is active, and whether the data dir is writable. Saves most support questions. | 4 |

## 12. Build order (each milestone is shippable)

1. **Skeleton:** args, exit codes, path shortcut, `--init`, wrapper. *Done when `gt ..` and `gt C:\Windows` work in a fresh terminal.*
2. **Walker + resolver + decisions:** tiers, qualifiers, the decision file, stale-entry cleanup. *Done when `goto documents` works with no Everything and the second run does no search.*
3. **Picker and "too many" handling:** includes non-interactive mode.
4. **Everything backend:** embed DLL, auto-start, fallback, `--doctor`, suggestions for narrowing.
5. **Ranking, frecency, `--explain`, prompt hook.**
6. **Extras:** aliases, completion, typo/abbreviation tiers, `--wrong`.
7. **Polish:** AOT publish, startup-time check, `--install`.

**Performance budget:** a remembered decision completes in under 50 ms total. A warm Everything query completes in under 100 ms. The walker is capped at 3 s.

## 13. Edge cases and acceptance tests

**Edge cases to handle explicitly:**

- Folders with spaces (`goto "my docs"`; each arg is a single term).
- Non-ASCII names (UTF-8 end to end).
- Paths over 260 characters.
- Network and UNC drives (walker skips non-fixed drives by default).
- Two terminals writing at once (mutex plus atomic rename).
- Corrupt or hand-edited data files (skip bad lines, never crash).
- First run with no data dir (create it).
- Access-denied folders (the walker ignores them; the wrapper surfaces `Set-Location` errors).
- User terms containing Everything operators (quote and escape).

**Acceptance tests** (use a fake `IFolderSearcher` plus a generated temp tree for unit and integration tests):

1. One folder named `documents` → goes there directly.
2. No exact match but a folder containing `documents` → T3 picks it up.
3. 40 matches with no clear winner → "be more specific" plus suggested commands, exit 2.
4. `goto user documents` → only folders named `documents` with `user` in an ancestor name.
5. Run `goto documents` twice → the second run reports source `decision` in `--explain` and performs no search.
6. Delete the remembered folder → the stale entry is removed and the search reruns.
7. Everything not running → fallback with a one-line stderr hint.
8. A folder named `Café` → arrives intact in PowerShell 5.1 and 7.
9. Picker cancelled with Esc → exit 2 and no `cd`.
