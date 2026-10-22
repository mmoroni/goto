namespace Goto.Shell;

internal static class ProfileInstallBlock
{
    public const string BeginMarker = "# >>> goto >>>";
    public const string EndMarker = "# <<< goto <<<";

    public static string Build(string exePath, bool withPromptHook)
    {
        string initArgs = withPromptHook ? "--init powershell --with-prompt-hook" : "--init powershell";
        // PowerShell splits a captured native process's multi-line stdout into an array of
        // per-line strings. Piping that array straight into Invoke-Expression invokes it ONCE
        // PER LINE (its -Command parameter binds from the pipeline), not once for the whole
        // script -- breaking on any blank line (empty-string error) and on any construct that
        // spans multiple lines (e.g. `function goto { ... }`, split mid-brace). Out-String joins
        // the array back into one multi-line string first, so Invoke-Expression sees it whole.
        return
            $"{BeginMarker}\n" +
            $"(& '{exePath}' {initArgs} | Out-String) | Invoke-Expression\n" +
            $"{EndMarker}\n";
    }
}
