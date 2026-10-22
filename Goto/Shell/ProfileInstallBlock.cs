namespace Goto.Shell;

internal static class ProfileInstallBlock
{
    public const string BeginMarker = "# >>> goto >>>";
    public const string EndMarker = "# <<< goto <<<";

    public static string Build(string exePath, bool withPromptHook)
    {
        string initArgs = withPromptHook ? "--init powershell --with-prompt-hook" : "--init powershell";
        return
            $"{BeginMarker}\n" +
            $"(& '{exePath}' {initArgs}) | Invoke-Expression\n" +
            $"{EndMarker}\n";
    }
}
