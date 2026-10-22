# goto (`gt`)

A directory-jumping CLI for Windows PowerShell: type part of a folder's name instead of its full
path. `gt documents` cds you to `Documents` — wherever it actually lives.

```
PS C:\> gt documents
PS C:\Users\you\Documents>
```

It learns from what you actually use: folders you jump to (or `cd` into, if you opt in) get
remembered, so an ambiguous name resolves to the right place more confidently over time.

Requires [Everything](https://www.voidtools.com/) (voidtools) installed and running — `gt` is a
thin, fast client over its index, not its own filesystem scanner. `gt --doctor` checks reachability.

## Features

- **Fast lookup** via the Everything search engine's index — typically single-digit milliseconds.
- **Frecency ranking** — recency + visit count + how close a candidate is to your current
  directory — so a clear winner resolves instantly; a real tie opens an interactive picker.
- **Typos and abbreviations**: `gt dl` → `Downloads`, a mistyped name suggests the closest match.
- **Bookmarks** (`gt --alias work D:\Clients\Acme`, then `gt @work` or `gt work`).
- **Tab completion** and a **Ctrl+G** hotkey to browse everywhere you've been.
- **`gt -e`/`gt -c`** open the result in Explorer or VS Code instead of `cd`-ing there.
- Ships as a single ~1.2 MB native executable — no .NET runtime install required.

## Install

1. Install and run [Everything](https://www.voidtools.com/) (voidtools) — `gt` has no filesystem
   scanner of its own and won't work without it.
2. Get `goto.exe` — either grab a built release, or [build it yourself](#building-from-source).
3. Run it once to wire it into your PowerShell profile(s):
   ```powershell
   .\goto.exe --install
   ```
   This installs into both Windows PowerShell 5.1's and `pwsh` 7's profile, whichever are present.
4. Open a new terminal (or `. $PROFILE`) and you're set — `gt`/`goto` are now shell functions.

Prefer not to touch your profile? `goto.exe --init powershell | Out-String | Invoke-Expression`
loads it for the current session only.

## Usage

```
gt <term> [term...]       Resolve <term> to a folder and cd there
gt -                      Jump back to the previous directory
gt -l, --list <term>      List every ranked match, don't cd
gt -p, --pick <term>      Force the picker instead of auto-resolving
gt -e, --explore <term>   Open the result in Explorer instead of cd-ing
gt -c, --code <term>      Open the result in VS Code instead of cd-ing
gt --wrong                Last resolution was wrong -- exclude it and resolve again
gt --history               Browse everywhere you've visited (also bound to Ctrl+G)
gt --alias NAME PATH       Bookmark PATH as @NAME
gt --doctor                Check Everything reachability and the data directory
```

Run `gt --help` for the full reference, including multi-term queries, aliases, and `--forget`.

## How it works

`goto.exe` does all the resolution work and prints only the destination path to stdout; a small
PowerShell function (installed by `--install`, or printed by `--init powershell`) calls it and runs
`Set-Location`. A child process can't change its parent shell's working directory, hence the split
— the same pattern tools like `zoxide` use.

Everything it remembers lives in plain TSV files under `%LOCALAPPDATA%\goto` (or `%GOTO_HOME%` if
set): visit counts, remembered decisions, aliases. Nothing phones home.

The full design — ranking formula, on-disk file formats, search tiers, the PowerShell integration
details — is written up in [`docs/specs/00-design-spec.md`](docs/specs/00-design-spec.md), with the
incremental build order and implementation notes in [`docs/specs/`](docs/specs/README.md).

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/) and Visual Studio Build Tools' "Desktop
development with C++" workload (native AOT needs the MSVC linker).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

Output: `Goto\bin\Release\net10.0\win-x64\publish\goto.exe`.

Run the test suite with:

```powershell
dotnet test Goto.Tests/Goto.Tests.csproj
```

See [`CLAUDE.md`](CLAUDE.md) for a deeper tour of the codebase layout and internal design
decisions.

## Third-party

`Everything64.dll` is bundled (embedded, extracted at runtime) under the voidtools Everything SDK's
MIT-style license — see [`docs/third-party/Everything-SDK-LICENSE.txt`](docs/third-party/Everything-SDK-LICENSE.txt).

## Development cost

The entire tool — all 7 design-spec milestones plus post-milestone polish — was built with
[Claude Code](https://claude.com/claude-code) in a single session:

| | |
|---|---|
| API cost | $84.45 |
| API duration | 3h 14m 48s |
| Wall-clock duration | 7h 22m 2s |
| Code changes | +10,062 / −419 lines |

(`claude-sonnet-5`, 98% of input tokens served from prompt cache.)
