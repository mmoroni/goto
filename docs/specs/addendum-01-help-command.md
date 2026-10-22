# Addendum 1: `-h`/`--help`

**Status: Implemented.**

Unlike `milestone-0N-*.md`, this isn't part of the original design spec's 7-milestone build order
(`docs/specs/00-design-spec.md` §12) — the spec never calls for a `--help` flag at all. This is a
post-Milestone-7 usability fix, made after direct user feedback that the bare usage line (`usage:
gt <term> [term...] | gt --init powershell | gt --install`) didn't explain what `--init`/`--install`
actually do, and gave no indication of the other dozen-plus flags Milestones 1–7 had accumulated.

## Motivation

By the end of Milestone 7, `ArgParser` recognized 20 flags across navigation, bookmarks, diagnostics,
and shell integration, but the only user-facing summary was the one-line usage string shown on a bare
invocation. There was no `--help`, and no single place a user (or a future Claude Code session) could
read to see what every flag does.

## What changed

- **`Goto/Cli/HelpText.cs`** (new): two constants.
  - `ShortUsage` — the terse one-liner shown on a bare usage error (exit 4), now pointing at
    `--help` instead of trying to enumerate commands inline.
  - `Full` — the complete reference: one line per flag, grouped by purpose (navigating, first-time
    setup, bookmarks & memory, diagnostics), plus the exit code table. Deliberately omits `--add`
    and `--complete` from the main listing (documented in a closing note instead) — both are
    internal plumbing consumed only by the generated PowerShell scripts (the prompt hook and tab
    completer respectively), not something a user would type by hand; listing them alongside
    user-facing commands would bury the ones people actually need.
- **`Goto/Cli/ArgParser.cs`**: new `Help` bool on `ParsedArgs`, recognizing `-h`/`--help`.
- **`Goto/Program.cs`**: `RunHelp()` — checked first in `Run()`, before every other meta-command
  branch (`--init`, `--install`, `--doctor`, etc.), consistent with how `--help` takes priority over
  everything else in most CLIs. Prints `HelpText.Full` to stdout via plain `Console.WriteLine`
  (not the `RawOutput` chokepoint) and returns `ExitCode.Success` — this is the one meta-command
  whose output is purely for human eyes, never piped/consumed by a script, so it doesn't need
  `RawOutput`'s byte-exact non-ASCII-safe path (that chokepoint exists specifically for paths and
  for `--init`'s script text going to `Invoke-Expression`; `--help`'s own text has neither concern).

## Acceptance criteria

- `gt --help` and `gt -h` print the full reference to stdout and exit 0.
- A bare `gt` (no args, no flags) still exits 4, now pointing at `gt --help` instead of trying to
  cram a summary into one line.
- The full text accurately describes every user-facing flag's actual behavior (verified against
  `Resolver.cs`'s real `--list`/`--pick`/dominant-winner semantics, not just flag names).

## Key files

- `Goto/Cli/HelpText.cs` (new)
- `Goto/Cli/ArgParser.cs` (`Help` flag)
- `Goto/Program.cs` (`RunHelp`, dispatch order, `ShortUsage` on bare invocation)
- `Goto.Tests/Cli/ArgParserTests.cs` (extended: `-h`/`--help` recognized)
