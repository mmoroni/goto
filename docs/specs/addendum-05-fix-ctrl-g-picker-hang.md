# Addendum 5: Fix Ctrl+G hanging instead of showing the picker

**Status: Implemented.**

Like the other addenda, a user-reported bug fix, not new spec-driven scope. Reported directly:
pressing Ctrl+G "shows nothing but blocks any other input from the console until I send a Ctrl+C."

## Root cause

Milestone 7's original `HistoryHotkeyBlock` (`docs/specs/milestone-07-polish.md`) ran the picker
*inside* the `Set-PSReadLineKeyHandler` script block itself: `& $GOTO_EXE --history`, capture the
result, `Set-Location`. But a custom key handler's script block executes inside PSReadLine's own
input-reading loop. `Picker.Run` reads raw console keys via `Console.ReadKey` from a freshly
launched child process — and that child process is now racing PSReadLine for the same console input
stream. In practice, nothing got through to the picker except Ctrl+C, because PSReadLine keeps a
dedicated watcher for Ctrl+C specifically (so a user can always abort a slow/stuck custom handler) —
every other key the user pressed never reached the picker's `Console.ReadKey` at all, matching
exactly what was reported: no visible picker, totally unresponsive except to Ctrl+C.

This is a known class of gotcha for PSReadLine integrations that need to hand off to a genuinely
interactive external program (not just manipulate the command-line text) — `PSFzf` and `zoxide`-style
PSReadLine bindings hit the same constraint and solve it the same way (see "The fix" below). It
wasn't caught during Milestone 7's own manual verification because that verification confirmed the
*script content* registered without error and that `gt --history` worked correctly as a standalone,
top-level command — it never actually pressed Ctrl+G in a live interactive session with real
keystrokes beyond Enter/Esc, which is the one scenario that exposes this.

## The fix

Don't run the picker from inside the handler at all. Instead, the handler inserts the real command
as pending input and submits it with `AcceptLine()`, so it runs afterward as a completely ordinary
top-level command — outside the handler, outside PSReadLine's input loop — where `Console.ReadKey`
behaves normally:

```powershell
Set-PSReadLineKeyHandler -Chord 'Ctrl+g' -ScriptBlock {
    [Microsoft.PowerShell.PSConsoleReadLine]::RevertLine()
    [Microsoft.PowerShell.PSConsoleReadLine]::Insert('gt --history')
    [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine()
}
```

This also means the handler no longer needs its own `$out`/`$code`/`Set-Location` logic, or the
`InvokePrompt()` call to force a redraw afterward — `gt --history` running as a normal command
reuses the existing `goto` function's capture-and-`Set-Location` logic verbatim, and a normal
command's normal post-execution prompt redraw happens for free. Net effect: the handler shrank from
a dozen lines of duplicated capture logic to three lines that just type-and-submit.

## Why this didn't require any exe-side change

`goto.exe --history`'s own behavior (`RunHistory` in `Program.cs`, `Picker.Run`) was never the
problem — it behaves identically whether invoked as a normal command or (incorrectly) from inside a
key handler; the picker's `Console.ReadKey` calls are the same either way. The bug was entirely in
*how* the generated PowerShell script invoked it. Since `--install`/`--init powershell` regenerate
the integration script fresh from the current build every time a shell starts (the installed profile
line is `(& goto.exe --init powershell | Out-String) | Invoke-Expression`, not a static dump),
redeploying the fixed exe was sufficient to fix an already-installed profile — no profile file edit
was needed, unlike `addendum-02`'s `Invoke-Expression` fix (which changed the profile *template*
`--install` writes, not something `--init powershell`'s own output could self-correct).

## Key files

- `Goto/Shell/PowerShellInit.cs` (`HistoryHotkeyBlock`)
