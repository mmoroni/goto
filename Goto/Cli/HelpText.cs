namespace Goto.Cli;

internal static class HelpText
{
    // One-liner shown on a bare usage error (exit 4) -- the full text below is reserved for an
    // explicit `gt --help`/`gt -h` request (exit 0), to keep "you typed it wrong" terse.
    public const string ShortUsage = "usage: gt <term> [term...]  (see 'gt --help' for all commands)";

    public const string Full = """
        gt -- jump to a folder by name instead of typing out its path.

        USAGE
          gt <term> [term...]       Resolve <term(s)> to a folder and cd there.
                                    Multiple terms narrow an ambiguous match, e.g.
                                    'gt proj backend' looks for "backend" under a
                                    folder matching "proj". A single term that's
                                    already a path (., .., ~, C:\x, a relative
                                    folder) is used as-is, no search needed.
          gt -                     Jump back to the previous directory (like `cd -`).

        FIRST-TIME SETUP
          gt --install              Wire gt/goto into your PowerShell profile(s) --
                                    persists across sessions. Installs into both
                                    Windows PowerShell 5.1's and pwsh 7's profile,
                                    whichever are present on this machine.
          gt --init powershell       Print the integration script to stdout instead
                                    of installing it, for a one-off session:
                                    gt --init powershell | Out-String | Invoke-Expression
          --with-prompt-hook         (with --install or --init) also learn from a
                                    plain `cd`, not just `gt` -- adds a small
                                    per-prompt-redraw cost, so it's opt-in.

        NAVIGATING
          gt -l, --list <term>      List every ranked match; don't cd.
          gt -p, --pick <term>      Force the picker instead of auto-resolving.
          gt --wrong                 Last resolution was wrong -- exclude it and
                                    resolve again (reopens the picker if ambiguous).
          gt --history               Browse every folder you've visited (bound to
                                    Ctrl+G once --install has run).
          gt -e, --explore <term>   Open the result in Explorer instead of cd-ing.
          gt -c, --code <term>      Open the result in VS Code instead of cd-ing.
                                    (-e/-c combine with -p: pick, then open; they
                                    don't combine with -l or each other.)

        BOOKMARKS & MEMORY
          gt --alias NAME PATH      Save PATH as a bookmark (gt @NAME or gt NAME).
          gt --unalias NAME         Remove a bookmark.
          gt --aliases              List all bookmarks.
          gt --forget <term...>     Forget the remembered decision for <term>.

        DIAGNOSTICS
          gt --doctor                Check Everything reachability, which search
                                    backend is active, and the data directory.
          gt --explain <term>        Show why <term> resolved the way it did
                                    (source + ranked candidate score breakdown).

        Exit codes: 0 success, 1 no match, 2 ambiguous/cancelled, 3 backend/config
        error, 4 usage error.

        (--add and --complete are internal, used by the generated PowerShell
        integration scripts -- not meant to be typed by hand.)
        """;
}
