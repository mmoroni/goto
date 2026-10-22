using Goto.Ui;

namespace Goto.Tests.Ui;

public class PickerStateTests
{
    private static ConsoleKeyInfo Key(char c, ConsoleKey key, bool control = false) =>
        new(c, key, shift: false, alt: false, control: control);

    private static ConsoleKeyInfo Letter(char c) => Key(c, ConsoleKey.A); // ConsoleKey value irrelevant for letters
    private static readonly ConsoleKeyInfo UpArrow = Key('\0', ConsoleKey.UpArrow);
    private static readonly ConsoleKeyInfo DownArrow = Key('\0', ConsoleKey.DownArrow);
    private static readonly ConsoleKeyInfo EnterKey = Key('\r', ConsoleKey.Enter);
    private static readonly ConsoleKeyInfo EscapeKey = Key('\0', ConsoleKey.Escape);
    private static readonly ConsoleKeyInfo Backspace = Key('\b', ConsoleKey.Backspace);
    private static readonly ConsoleKeyInfo CtrlC = Key('\u0003', ConsoleKey.C, control: true);

    private static PickerState MakeState(params string[] candidates) => new(candidates);

    [Fact]
    public void DownArrow_MovesSelectionForward_ClampsAtLastRow()
    {
        var state = MakeState("a", "b", "c");
        state.HandleKey(DownArrow);
        state.HandleKey(DownArrow);
        state.HandleKey(DownArrow); // one past the end
        Assert.Equal(2, state.SelectedIndex);
    }

    [Fact]
    public void UpArrow_MovesSelectionBackward_ClampsAtFirstRow()
    {
        var state = MakeState("a", "b", "c");
        state.HandleKey(UpArrow); // already at 0
        Assert.Equal(0, state.SelectedIndex);
    }

    [Fact]
    public void Enter_ReturnsSelectedCandidate()
    {
        var state = MakeState("a", "b", "c");
        state.HandleKey(DownArrow);
        var action = state.HandleKey(EnterKey);
        Assert.Equal(PickerActionKind.Selected, action.Kind);
        Assert.Equal("b", action.Path);
    }

    [Fact]
    public void Escape_ReturnsCancelled()
    {
        var state = MakeState("a", "b");
        var action = state.HandleKey(EscapeKey);
        Assert.Equal(PickerActionKind.Cancelled, action.Kind);
    }

    [Fact]
    public void CtrlC_ReturnsCancelled()
    {
        var state = MakeState("a", "b");
        var action = state.HandleKey(CtrlC);
        Assert.Equal(PickerActionKind.Cancelled, action.Kind);
    }

    [Fact]
    public void TypingLetters_AppendsToFilter_NarrowsVisibleCandidates()
    {
        var state = MakeState(@"C:\tree\documents", @"C:\tree\downloads");
        state.HandleKey(Letter('d'));
        state.HandleKey(Letter('o'));
        state.HandleKey(Letter('c'));
        Assert.Equal("doc", state.Filter);
        Assert.Single(state.VisibleCandidates);
        Assert.Equal(@"C:\tree\documents", state.VisibleCandidates[0]);
    }

    [Fact]
    public void Backspace_RemovesLastFilterChar_WidensVisibleCandidatesBack()
    {
        var state = MakeState(@"C:\tree\documents", @"C:\tree\downloads");
        state.HandleKey(Letter('d'));
        state.HandleKey(Letter('o'));
        state.HandleKey(Letter('c'));
        Assert.Single(state.VisibleCandidates);

        state.HandleKey(Backspace);
        state.HandleKey(Backspace);
        state.HandleKey(Backspace);
        Assert.Equal("", state.Filter);
        Assert.Equal(2, state.VisibleCandidates.Count);
    }

    [Fact]
    public void DigitKey_WhenFilterEmpty_JumpsToThatRow()
    {
        var state = MakeState("a", "b", "c");
        state.HandleKey(Key('3', ConsoleKey.D3));
        Assert.Equal(2, state.SelectedIndex);
    }

    [Fact]
    public void DigitKey_AfterTypingStarted_BecomesFilterCharacter()
    {
        var state = MakeState("alpha", "beta");

        state.HandleKey(Letter('x')); // non-digit: starts the filter (Filter.Length becomes > 0)
        state.HandleKey(Key('2', ConsoleKey.D2)); // Filter is non-empty now, so this appends, not jumps

        Assert.Equal("x2", state.Filter);
    }

    [Fact]
    public void DigitKey_BeyondVisibleRowCount_IsNoOp()
    {
        var state = MakeState("a", "b");
        state.HandleKey(Key('9', ConsoleKey.D9));
        Assert.Equal(0, state.SelectedIndex);
    }

    [Fact]
    public void Enter_WithNoVisibleCandidates_ReturnsContinue()
    {
        var state = MakeState("alpha", "beta");
        state.HandleKey(Letter('z')); // matches nothing
        Assert.Empty(state.VisibleCandidates);

        var action = state.HandleKey(EnterKey);
        Assert.Equal(PickerActionKind.Continue, action.Kind);
    }

    [Fact]
    public void SelectedIndex_ClampedWhenFilterShrinksVisibleList()
    {
        var state = MakeState(@"C:\tree\documents", @"C:\tree\downloads", @"C:\tree\other-doc");
        state.HandleKey(DownArrow);
        state.HandleKey(DownArrow); // selected index 2 ("other-doc")

        state.HandleKey(Letter('d'));
        state.HandleKey(Letter('o'));
        state.HandleKey(Letter('w')); // narrows to just "downloads"

        Assert.Single(state.VisibleCandidates);
        Assert.Equal(0, state.SelectedIndex);
    }
}
