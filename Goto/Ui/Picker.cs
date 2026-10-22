namespace Goto.Ui;

internal static class Picker
{
    /// <returns>the selected path, or null if the user cancelled (Esc/Ctrl+C).</returns>
    public static string? Run(IReadOnlyList<string> candidates)
    {
        var state = new PickerState(candidates);
        int linesDrawn = 0;

        bool originalTreatCtrlC = Console.TreatControlCAsInput;
        // Without this, Console.ReadKey never sees Ctrl+C as a key at all -- the runtime's
        // default handler intercepts it first and tears down the process.
        Console.TreatControlCAsInput = true;

        using Vt.Scope vt = new();
        Console.Error.Write("\u001b[?25l"); // hide cursor
        try
        {
            while (true)
            {
                linesDrawn = Render(state, linesDrawn);
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                PickerAction action = state.HandleKey(key);

                if (action.Kind == PickerActionKind.Selected)
                    return action.Path;
                if (action.Kind == PickerActionKind.Cancelled)
                    return null;
            }
        }
        finally
        {
            Console.Error.Write("\u001b[?25h"); // restore cursor, even on exception
            Console.Error.WriteLine();
            Console.TreatControlCAsInput = originalTreatCtrlC;
        }
    }

    private static int Render(PickerState state, int previousLines)
    {
        if (previousLines > 0)
            Console.Error.Write($"\u001b[{previousLines}A\u001b[0J"); // cursor up N, erase to end

        for (int i = 0; i < state.VisibleCandidates.Count; i++)
        {
            string path = state.VisibleCandidates[i];
            string line = $"  {i + 1}. {HighlightLeaf(path)}";
            Console.Error.WriteLine(i == state.SelectedIndex ? $"\u001b[7m{line}\u001b[0m" : line);
        }

        Console.Error.WriteLine($"> {state.Filter}");
        return state.VisibleCandidates.Count + 1;
    }

    private static string HighlightLeaf(string path)
    {
        string leaf = Path.GetFileName(path);
        int idx = path.LastIndexOf(leaf, StringComparison.OrdinalIgnoreCase);
        return idx < 0
            ? path
            : path[..idx] + $"\u001b[1m{path.Substring(idx, leaf.Length)}\u001b[0m" + path[(idx + leaf.Length)..];
    }
}
