namespace Goto.Ui;

internal enum PickerActionKind { Continue, Selected, Cancelled }

internal readonly record struct PickerAction(PickerActionKind Kind, string? Path)
{
    public static readonly PickerAction Continue = new(PickerActionKind.Continue, null);
    public static readonly PickerAction Cancelled = new(PickerActionKind.Cancelled, null);
    public static PickerAction Selected(string path) => new(PickerActionKind.Selected, path);
}

internal sealed class PickerState
{
    private readonly string[] _all;

    public string Filter { get; private set; } = "";
    public IReadOnlyList<string> VisibleCandidates { get; private set; }
    public int SelectedIndex { get; private set; }

    public PickerState(IReadOnlyList<string> candidates)
    {
        _all = candidates.ToArray();
        VisibleCandidates = _all;
        SelectedIndex = _all.Length > 0 ? 0 : -1;
    }

    public PickerAction HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
            return PickerAction.Cancelled;

        if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            return PickerAction.Cancelled;

        if (key.Key == ConsoleKey.Enter)
        {
            return SelectedIndex >= 0 && SelectedIndex < VisibleCandidates.Count
                ? PickerAction.Selected(VisibleCandidates[SelectedIndex])
                : PickerAction.Continue;
        }

        if (key.Key == ConsoleKey.UpArrow)
        {
            Move(-1);
            return PickerAction.Continue;
        }

        if (key.Key == ConsoleKey.DownArrow)
        {
            Move(1);
            return PickerAction.Continue;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (Filter.Length > 0)
            {
                Filter = Filter[..^1];
                Recompute();
            }
            return PickerAction.Continue;
        }

        // Digits 1-9 jump within the visible (filtered) rows ONLY while no filter text has been
        // typed yet; once typing has started, digits become ordinary filter characters instead,
        // so folder names containing digits remain filterable.
        if (Filter.Length == 0 && key.KeyChar is >= '1' and <= '9')
        {
            int row = key.KeyChar - '1';
            if (row < VisibleCandidates.Count)
                SelectedIndex = row;
            return PickerAction.Continue;
        }

        if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0')
        {
            Filter += key.KeyChar;
            Recompute();
            return PickerAction.Continue;
        }

        return PickerAction.Continue;
    }

    private void Move(int delta)
    {
        if (VisibleCandidates.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, VisibleCandidates.Count - 1);
    }

    private void Recompute()
    {
        VisibleCandidates = Filter.Length == 0
            ? _all
            : _all.Where(p => Path.GetFileName(p).Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToArray();

        SelectedIndex = VisibleCandidates.Count == 0 ? -1 : Math.Clamp(SelectedIndex, 0, VisibleCandidates.Count - 1);
    }
}
