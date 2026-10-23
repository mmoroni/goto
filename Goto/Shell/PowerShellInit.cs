namespace Goto.Shell;

internal static class PowerShellInit
{
    public const string Script = """
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
        """;

    // Optional (spec section 5, Phase 6): tab completion. Unlike the prompt hook below, this has
    // no recurring runtime cost (it only runs when the user presses Tab), so it's unconditional --
    // always appended, no opt-in flag needed. The base Script constant above stays untouched.
    public const string TabCompletionBlock = """

        Register-ArgumentCompleter -Native -CommandName goto, gt -ScriptBlock {
            param($wordToComplete, $commandAst, $cursorPosition)
            # "--complete=$wordToComplete" as ONE token, not two: PowerShell silently drops a
            # literal empty-string ARGUMENT when invoking a native exe (confirmed empirically,
            # in any position, not just trailing) -- exactly $wordToComplete when the user
            # presses Tab with nothing typed yet. Folding it into a single, never-empty token
            # sidesteps the problem entirely instead of relying on argument-position workarounds.
            $out = & $global:GOTO_EXE "--complete=$wordToComplete" 2>$null
            foreach ($line in $out) {
                if ($line) {
                    [System.Management.Automation.CompletionResult]::new($line, $line, 'ParameterValue', $line)
                }
            }
        }
        """;

    // Optional (spec section 11, Phase 7): a PSReadLine Ctrl+G hotkey that opens the picker over
    // everything you've visited. Unconditional like TabCompletionBlock above (it only runs when
    // the user actually presses Ctrl+G -- no recurring cost), not opt-in like the prompt hook
    // below. Wrapped in try/catch: PSReadLine isn't guaranteed loaded in every host (ISE,
    // non-interactive/scripted profile loads, or a user who removed the module), and a missing
    // cmdlet here must never break profile load.
    //
    // The handler does NOT run the picker itself. A custom key handler's script block executes
    // *inside* PSReadLine's own input-reading loop; launching a child process that reads raw
    // console keys (Picker.Run's Console.ReadKey) from there races PSReadLine for the same input
    // stream -- confirmed empirically: nothing but Ctrl+C (PSReadLine's handler-abort watcher) got
    // through, exactly as if the picker weren't receiving any other keystroke at all. The standard
    // fix (the same pattern fzf/zoxide-style PSReadLine integrations use): insert the real command
    // as pending input and submit it, so the picker runs as an ordinary top-level command --
    // completely outside the handler -- where Console.ReadKey behaves normally. Reuses the `goto`
    // function verbatim (its own $out/$code/Set-Location logic already does exactly what's needed),
    // so no duplicate capture logic lives here.
    public const string HistoryHotkeyBlock = """

        try {
            Set-PSReadLineKeyHandler -Chord 'Ctrl+g' -ScriptBlock {
                [Microsoft.PowerShell.PSConsoleReadLine]::RevertLine()
                [Microsoft.PowerShell.PSConsoleReadLine]::Insert('gt --history')
                [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine()
            }
        } catch {
            # PSReadLine not loaded/available in this host -- no-op rather than breaking the profile.
        }
        """;

    // Optional (spec section 5, Phase 5): learn from plain `cd`, not just `goto`. Not part
    // of the verbatim base script above -- opt-in via `--init powershell --with-prompt-hook`,
    // since wrapping `prompt` shells out to goto.exe on every prompt redraw, a continuous
    // latency cost users should consciously choose rather than inherit silently.
    public const string PromptHookAddendum = """

        $global:__gotoLastPwd = $PWD.Path
        $global:__gotoOrigPrompt = $function:prompt
        function prompt {
            $__gotoCode = $LASTEXITCODE
            if ($PWD.Path -ne $global:__gotoLastPwd) {
                $global:__gotoLastPwd = $PWD.Path
                & $global:GOTO_EXE --add $PWD.Path *> $null
            }
            $LASTEXITCODE = $__gotoCode
            & $global:__gotoOrigPrompt
        }
        """;
}
