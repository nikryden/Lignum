using Lignum.Core;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Lignum.App.Viewer;

/// <summary>
/// The caret: a <see cref="FilePosition"/> (file line, character index) bound two-way to the view model. Clicking
/// places it; the arrow keys, Home/End and PageUp/PageDown move it and scroll to keep it in view. The view maps it
/// to a row through <see cref="LineSource.RowOfLine"/> and to a display column with the tab rules, so it stays
/// put when the view scrolls or the source is a result set.
/// </summary>
public sealed partial class LogView
{
    public static readonly DependencyProperty CaretProperty = DependencyProperty.Register(
        nameof(Caret), typeof(FilePosition), typeof(LogView),
        new FrameworkPropertyMetadata(default(FilePosition),
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private const double CaretWidth = 2;

    /// <summary>Where the caret is in the file. Not clamped: a line past the end is drawn nowhere.</summary>
    public FilePosition Caret
    {
        get => (FilePosition)GetValue(CaretProperty);
        set => SetValue(CaretProperty, value);
    }

    /// <summary>
    /// Scrolls so the caret's row and column are on screen: a nearby caret scrolls the least needed, a far one
    /// (a jump) is put in the middle of the view.
    /// </summary>
    public void RevealCaret()
    {
        if (Source is not { } source || source.RowCount == 0) return;
        var caret = Caret;
        var row = source.RowOfLine(caret.Line);
        var page = Math.Max(1, ViewportRows);
        var first = FirstRow;
        if (row < first - page || row >= first + 2L * page) SetCurrentValue(FirstRowProperty, row - page / 2);
        else if (row < first) SetCurrentValue(FirstRowProperty, row);
        else if (row >= first + page) SetCurrentValue(FirstRowProperty, row - page + 1);

        var column = CaretDisplayColumn(caret);
        var columns = Math.Max(1, ViewportColumns);
        if (column < FirstColumn) FirstColumn = Math.Max(0, column - columns / 4);
        else if (column >= FirstColumn + columns) FirstColumn = column - columns + 1;
    }

    /// <summary>The caret's display column; without the line's text loaded, tabs are assumed absent.</summary>
    private int CaretDisplayColumn(FilePosition caret) =>
        LoadedText(caret.Line) is { } text ? TabExpansion.IndexToColumn(text, TabWidth, caret.Column) : caret.Column;

    /// <summary>Text of a file line if it is among the rows read for the screen.</summary>
    private string? LoadedText(long line)
    {
        foreach (var row in _rows)
        {
            if (row.LineNumber == line) return row.Text;
        }

        return null;
    }

    /// <summary>Called at the end of <see cref="OnRender"/>; draws a bar in the gap before the caret column.</summary>
    private void DrawCaret(DrawingContext dc, long firstRow, int rowCount, double textLeft)
    {
        if (!IsKeyboardFocused && !IsKeyboardFocusWithin) return;
        var caret = Caret;
        for (var i = 0; i < rowCount; i++)
        {
            if (TryGetRow(firstRow + i) is not { } row || row.LineNumber != caret.Line) continue;
            var column = TabExpansion.IndexToColumn(row.Text, TabWidth, caret.Column) - FirstColumn;
            if (column < 0) return;
            var x = textLeft + column * _charWidth;
            if (x > ActualWidth) return;
            dc.DrawRectangle(Foreground, null, new Rect(Math.Max(textLeft, x - CaretWidth / 2), i * _lineHeight,
                CaretWidth, _lineHeight));
            return;
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    /// <summary>A left click puts the caret at the nearest character gap of the clicked line.</summary>
    private void PlaceCaret(Point point)
    {
        if (Source is not { } source || source.RowCount == 0) return;
        var position = Geometry.PointToPosition(point);
        var row = Math.Clamp(position.Row, 0, source.RowCount - 1);
        if (TryGetRow(row) is not { } loaded) return; // not drawn yet: nothing to click on
        SetCurrentValue(CaretProperty,
            new FilePosition(loaded.LineNumber, TabExpansion.ColumnToIndex(loaded.Text, TabWidth, position.Column)));
        RevealCaret();
    }

    /// <summary>Moves the caret for an unmodified navigation key. Returns false for other keys.</summary>
    private bool MoveCaret(Key key)
    {
        if (Source is not { } source || source.RowCount == 0) return false;
        var caret = Caret;
        var page = Math.Max(1, ViewportRows);
        var text = LoadedText(caret.Line);
        var length = text?.Length ?? int.MaxValue;
        switch (key)
        {
            case Key.Up: caret = ByRows(source, caret, -1); break;
            case Key.Down: caret = ByRows(source, caret, 1); break;
            case Key.PageUp:
                ScrollRows(-page);
                caret = ByRows(source, caret, -page);
                break;
            case Key.PageDown:
                ScrollRows(page);
                caret = ByRows(source, caret, page);
                break;
            case Key.Left: caret = caret with { Column = Math.Max(0, Math.Min(caret.Column, length) - 1) }; break;
            case Key.Right: caret = caret with { Column = (int)Math.Min((long)caret.Column + 1, length) }; break;
            case Key.Home: caret = caret with { Column = 0 }; break;
            case Key.End:
                if (text is null) return true;
                caret = caret with { Column = text.Length };
                break;
            default: return false;
        }

        SetCurrentValue(CaretProperty, caret);
        RevealCaret();
        return true;
    }

    /// <summary>The caret moved by whole rows of the source, keeping its character index.</summary>
    private static FilePosition ByRows(LineSource source, FilePosition caret, long delta)
    {
        var row = Math.Clamp(source.RowOfLine(caret.Line) + delta, 0, source.RowCount - 1);
        return caret with { Line = source.Lines[row] };
    }
}
