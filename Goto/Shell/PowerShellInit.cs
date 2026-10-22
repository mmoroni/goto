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
