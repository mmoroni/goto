using System.Collections.Concurrent;
using Goto.Core;
using Goto.Store;

namespace Goto.Search;

internal sealed class WalkSearcher(Config config) : IFolderSearcher
{
    private static readonly EnumerationOptions EnumOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
    };

    public string Name => "walk";

    public bool LastSearchWasIncomplete { get; private set; }

    public bool TryConnect(out string? whyNot)
    {
        whyNot = null;
        return true;
    }

    public IReadOnlyList<string> FindFolders(
        string leaf, IReadOnlyList<string> qualifiers,
        MatchMode mode, int limit, CancellationToken ct)
    {
        LastSearchWasIncomplete = false;

        foreach (string root in ResolveRoots())
        {
            var bag = new ConcurrentBag<string>();

            try
            {
                IEnumerable<string> firstLevel;
                try
                {
                    firstLevel = Directory.EnumerateDirectories(root, "*", EnumOptions)
                        .Where(d => !config.IsExcluded(d))
                        .ToArray();
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    continue;
                }

                Parallel.ForEach(
                    firstLevel,
                    new ParallelOptions { CancellationToken = ct },
                    start => WalkBranch(start, leaf, mode, bag, limit, ct));
            }
            catch (OperationCanceledException)
            {
                LastSearchWasIncomplete = true;
                return bag.Distinct(StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
            }

            if (!bag.IsEmpty)
                return bag.Distinct(StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
        }

        return Array.Empty<string>();
    }

    private void WalkBranch(string start, string leaf, MatchMode mode, ConcurrentBag<string> bag, int limit, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            if (bag.Count >= limit)
                return;

            string current = stack.Pop();
            string name = Path.GetFileName(current);

            if (!string.IsNullOrEmpty(name) && Matcher.LeafMatches(name, leaf, mode))
                bag.Add(current);

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(current, "*", EnumOptions);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (string child in children)
            {
                if (!config.IsExcluded(child))
                    stack.Push(child);
            }
        }
    }

    private IEnumerable<string> ResolveRoots()
    {
        if (config.WalkRoots.Count > 0)
        {
            foreach (string root in config.WalkRoots)
                yield return root;
            yield break;
        }

        yield return Environment.CurrentDirectory;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                yield return drive.RootDirectory.FullName;
        }
    }
}
