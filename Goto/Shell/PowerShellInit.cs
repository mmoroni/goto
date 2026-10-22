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
}
