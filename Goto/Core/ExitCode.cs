namespace Goto.Core;

internal static class ExitCode
{
    public const int Success = 0;
    public const int NoMatch = 1;
    public const int AmbiguousOrCancelled = 2;
    public const int BackendOrConfigError = 3;
    public const int UsageError = 4;
}
