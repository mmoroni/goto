# Addendum 2: Two stdout-discipline bugs found via a real installed profile

**Status: Implemented.**

Like Addendum 1, not called for by the original design spec — a user-reported bug fix, found
because this was the first time the *actual installed* `pwsh` 7 profile was sourced in a real
session rather than just inspecting `--init powershell`'s printed script text or capturing it into
a variable. Both bugs had existed since earlier milestones; neither was a regression from a recent
change, but neither had been exercised by any of this project's manual verification until now.

## Bug 1: `(& exe --init powershell) | Invoke-Expression` only ever worked by accident

**Symptom** (as reported): opening a new `pwsh` session threw `Invoke-Expression: ... Cannot bind
argument to parameter 'Command' because it is an empty string` immediately followed by `Missing
closing '}' in statement block or type definition`.

**Root cause**: PowerShell splits a captured native process's multi-line stdout into an array of
per-line strings. `Invoke-Expression` binds its `-Command` parameter from the pipeline
(`ValueFromPipeline`), so piping that array into it invokes `Invoke-Expression` **once per line**,
not once for the whole script. A blank line between script sections becomes an empty-string
`-Command` call (error 1); a multi-line construct like `function goto { ... }` gets invoked one
line at a time, so `function goto {` is evaluated alone with no matching `}` (error 2). This exact
`(& exe args) | Invoke-Expression` line has been in `ProfileInstallBlock.Build` (and, before
Milestone 7's extraction, inline in Milestone 1's `RunInstall`) since Milestone 1 — it was never
actually broken by anything done in Milestone 7 or the `--help` addendum, it just had never been
exercised end-to-end: every milestone's manual verification captured `--init powershell`'s output
into a variable (`$out = & $exe args`) to inspect its *content*, never piped it straight into
`Invoke-Expression` inside a freshly started profile-loading session the way the installed line
actually does.

**Fix**: `Out-String` joins the line array back into a single multi-line string before
`Invoke-Expression` sees it — `(& exe args | Out-String) | Invoke-Expression`. Applied in
`ProfileInstallBlock.Build` (the only place in the codebase generating this pattern) and corrected
in `HelpText.Full`'s `--init powershell` usage example, which showed the same broken one-liner.

## Bug 2: `--doctor`/`--aliases`/`--help` tried to `cd` into their own output

**Found while verifying Bug 1's fix**, not separately reported: once the profile actually loads
correctly, `gt --help`, `gt --doctor`, and `gt --aliases` (invoked through the `goto`/`gt`
PowerShell function, not the raw exe) each failed with `Set-Location: Cannot find path '...' because
it does not exist` — the wrapper function does `Set-Location` on **any** non-empty stdout with exit
code 0 (`PowerShellInit.Script`'s `if ($code -eq 0 -and $out) { Set-Location ... }`), and all three
commands printed their human-facing report/listing/help text via plain `Console.WriteLine` (real
stdout) while returning `ExitCode.Success`. `RunDoctor` (Milestone 4) and `RunAliasesList`
(Milestone 6) had this bug from the start; `RunHelp` (Addendum 1) inherited the same mistake by
following `Console.WriteLine`'s use in those two existing functions as a (wrong) precedent, instead
of the golden rule's actual invariant.

**Fix**: switched all three to `Console.Error.WriteLine`, consistent with every other human-facing
message in the codebase and the golden rule in `CLAUDE.md` ("stdout carries ONLY the resolved
destination path... All human-facing messages... go to stderr"). `RunComplete` (`--complete`)
deliberately keeps `Console.WriteLine`/real stdout — it's invoked directly by
`Register-ArgumentCompleter`'s script block, never through the `goto`/`gt` wrapper function, so
there's no `Set-Location`-on-stdout hazard for it to avoid.

## Why this escaped every prior milestone's testing

All manual verification across Milestones 1–7 invoked `goto.exe`/the published exe directly
(`$out = & $exe args`, or `& $exe args` for diagnostics) — never through the actual `gt`/`goto`
PowerShell function defined by a real, dot-sourced profile. That's a real gap in this project's test
coverage, not just bad luck: direct-exe testing validates the exe's own behavior correctly, but
can't catch bugs that only manifest in the wrapper's stdout-triggers-`Set-Location` contract. Future
verification of any new meta-command that might be invoked via `gt`/`goto` (as opposed to flags like
`--complete`/`--add` that are only ever called directly by generated scripts) should include at
least one check through a real dot-sourced profile, not just direct exe invocation.

## Key files

- `Goto/Shell/ProfileInstallBlock.cs` (`Out-String` fix)
- `Goto/Cli/HelpText.cs` (corrected `--init powershell` example)
- `Goto/Program.cs` (`RunDoctor`, `RunAliasesList`, `RunHelp` switched to `Console.Error.WriteLine`;
  `RunComplete` given an explanatory comment for why it correctly stays on stdout)
