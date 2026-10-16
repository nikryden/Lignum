using Lignum.Core;

namespace Lignum.App.Viewer;

/// <summary>
/// A character selection stored as file positions: an <see cref="Anchor"/> where it started and a
/// <see cref="Caret"/> where it ends, independent of what is on screen, so it survives scrolling and can
/// span millions of lines. Empty when both are equal. UI-thread object (not thread-safe).
/// </summary>
public sealed class TextSelection
{
    public FilePosition Anchor { get; private set; }

    public FilePosition Caret { get; private set; }

    public FilePosition Start => Anchor <= Caret ? Anchor : Caret;

    public FilePosition End => Anchor <= Caret ? Caret : Anchor;

    public bool IsEmpty => Anchor == Caret;

    /// <summary>Raised when the anchor or caret changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Moves the caret; with <paramref name="extend"/> (Shift, drag) the anchor stays, otherwise it follows.</summary>
    public void MoveCaret(FilePosition position, bool extend)
    {
        Set(extend ? Anchor : position, position);
    }

    /// <summary>
    /// Selects every line: the anchor is the end of the last line (column <c>int.MaxValue</c>, "end of line")
    /// and the caret the start of the file, where a real column is known.
    /// </summary>
    public void SelectAll(long lineCount)
    {
        if (lineCount <= 0) Set(default, default);
        else Set(new FilePosition(lineCount - 1, int.MaxValue), new FilePosition(0, 0));
    }

    /// <summary>
    /// The selected character columns on <paramref name="line"/>, or null if none are. <c>To</c> is
    /// <c>int.MaxValue</c> when the selection continues past the end of the line (its line break is selected).
    /// </summary>
    public (int From, int To)? ColumnsOn(long line)
    {
        if (IsEmpty) return null;
        var start = Start;
        var end = End;
        if (line < start.Line || line > end.Line) return null;
        var from = line == start.Line ? start.Column : 0;
        var to = line == end.Line ? end.Column : int.MaxValue;
        return from < to ? (from, to) : null;
    }

    private void Set(FilePosition anchor, FilePosition caret)
    {
        if (anchor == Anchor && caret == Caret) return;
        Anchor = anchor;
        Caret = caret;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
