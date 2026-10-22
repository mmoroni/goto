using Goto.Cli;

namespace Goto.Core;

internal enum ResolveKind
{
    Success,
    NoMatch,
}

internal sealed record ResolveResult(ResolveKind Kind, string? Path);

internal static class Resolver
{
    public static ResolveResult Resolve(ParsedArgs args)
    {
        // Step 0 (spec section 7): single-term existing-path shortcut.
        if (args.Positionals.Count == 1 &&
            PathShortcut.TryResolve(args.Positionals[0], out string path))
        {
            return new ResolveResult(ResolveKind.Success, path);
        }

        // No ALIAS/DECISION store and no search tiers T1-T4 exist yet in M1.
        // Everything that isn't the path shortcut falls straight through to
        // "no tier matched" (spec step 4) -> exit code 1.
        return new ResolveResult(ResolveKind.NoMatch, null);
    }
}
