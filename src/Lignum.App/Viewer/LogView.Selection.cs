using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lignum.App.Themes;
using Lignum.Core;

namespace Lignum.App.Viewer;

/// <summary>
/// Selection: the view draws and edits a <see cref="TextSelection"/> (file positions, so it survives
/// scrolling). Drag selects, Shift+Click extends, Shift+arrows/Home/End/PageUp/PageDown extend from the
/// caret. The selection's caret is also written to <see cref="Caret"/>, so the caret bar and the status bar
/// follow it; the view model collapses the selection when the caret moves on its own (plain keys, Go to line).
/// Columns are character indexes, mapped to tab-expanded display columns with <see cref="TabExpansion"/>.
/// </summary>
public sealed partial class LogView
{
    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
        nameof(Selection), typeof(TextSelection), typeof(LogView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSelectionChanged));

    public static readonly DependencyProperty SelectionBackgroundProperty = DependencyProperty.Register(
        nameof(SelectionBackground), typeof(Brush), typeof(LogView),
        new FrameworkPropertyMetadata(SystemColors.HighlightBrush, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _dragging;
    private Point _dragPoint;
    private DispatcherTimer? _dragScrollTimer;

    /// <summary>The selection shown and edited by this view. Null: clicks only place the caret.</summary>
    public TextSelection? Selection
    {
        get => (TextSelection?)GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    public Brush SelectionBackground
    {
        get => (Brush)GetValue(SelectionBackgroundProperty);
        set => SetValue(SelectionBackgroundProperty, value);
    }

    private void InitializeSelection() =>
        SetResourceReference(SelectionBackgroundProperty, ThemeKeys.SelectionBackground);

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (LogView)d;
        if (e.OldValue is TextSelection old) old.Changed -= view.OnSelectionMoved;
        if (e.NewValue is TextSelection selection) selection.Changed += view.OnSelectionMoved;
    }

    private void OnSelectionMoved(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>Highlights the selected part of one row, behind its text (called from OnRender).</summary>
    private void DrawRowSelection(DrawingContext dc, DisplayRow row, double y, double textLeft, int firstColumn)
    {
        if (Selection?.ColumnsOn(row.LineNumber) is not { } range) return;
        var text = row.Text;
        var from = TabExpansion.IndexToColumn(text, TabWidth, range.From);
        var to = range.To > text.Length
            ? TabExpansion.IndexToColumn(text, TabWidth, text.Length) + 1 // the line break counts as one cell
            : TabExpansion.IndexToColumn(text, TabWidth, range.To);
        var left = textLeft + Math.Max(0, from - firstColumn) * _charWidth;
        var right = textLeft + Math.Max(0, (long)to - firstColumn) * _charWidth;
        if (right > left) dc.DrawRectangle(SelectionBackground, null, new Rect(left, y, right - left, _lineHeight));
    }

    /// <summary>Moves the selection's caret (extending or not) and the caret with it, then scrolls to it.</summary>
    private void SetSelectionCaret(TextSelection selection, FilePosition position, bool extend)
    {
        selection.MoveCaret(position, extend);
        SetCurrentValue(CaretProperty, position);
        RevealCaret();
    }

    // ---- mouse ----

    /// <summary>Left button down: place or (Shift) extend the selection and start dragging. False without a selection.</summary>
    private bool BeginSelectionDrag(MouseButtonEventArgs e)
    {
        if (Selection is not { } selection) return false;
        if (HitTest(e.GetPosition(this)) is not { } position) return true;
        SetSelectionCaret(selection, position, extend: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        _dragging = CaptureMouse();
        e.Handled = true;
        return true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        _dragPoint = e.GetPosition(this);
        ExtendDrag();
        // Outside the view, keep scrolling while the button is held, even if the mouse stops moving.
        var outside = _dragPoint.Y < 0 || _dragPoint.Y > ActualHeight || _dragPoint.X < _textLeft ||
                      _dragPoint.X > ActualWidth;
        if (outside && _dragScrollTimer is null)
        {
            _dragScrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Input,
                (_, _) => ExtendDrag(), Dispatcher);
            _dragScrollTimer.Start();
        }
        else if (!outside) StopDragScroll();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        EndDrag();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag();
    }

    private void EndDrag()
    {
        _dragging = false;
        StopDragScroll();
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void StopDragScroll()
    {
        _dragScrollTimer?.Stop();
        _dragScrollTimer = null;
    }

    private void ExtendDrag()
    {
        if (!_dragging || Selection is not { } selection) return;
        var p = _dragPoint;
        if (p.Y < 0) ScrollRows(-1);
        else if (p.Y > ActualHeight) ScrollRows(1);
        if (p.X < _textLeft && FirstColumn > 0) ScrollColumns(-1);
        else if (p.X > ActualWidth) ScrollColumns(1);
        var clamped = new Point(p.X, Math.Clamp(p.Y, 0, Math.Max(0, ViewportRows * LineHeight - 1)));
        if (HitTest(clamped) is not { } position) return;
        selection.MoveCaret(position, extend: true);
        SetCurrentValue(CaretProperty, position);
    }

    /// <summary>The file position nearest to a point in the view, or null when there are no rows.</summary>
    private FilePosition? HitTest(Point point)
    {
        if (Source is not { } source || source.RowCount == 0) return null;
        var position = Geometry.PointToPosition(point);
        var row = Math.Clamp(position.Row, 0, source.RowCount - 1);
        // A row not read yet (just scrolled in) has no text to map tabs with: assume none.
        var column = TryGetRow(row) is { } loaded
            ? TabExpansion.ColumnToIndex(loaded.Text, TabWidth, position.Column)
            : position.Column;
        return new FilePosition(source.Lines[row], column);
    }

    // ---- keyboard ----

    /// <summary>Shift+arrows, Home, End, PageUp, PageDown move the caret and extend the selection.</summary>
    private bool HandleSelectionKey(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Shift || Selection is not { } selection) return false;
        if (Source is not { } source || source.RowCount == 0) return false;

        var caret = selection.Caret;
        var length = LoadedText(caret.Line)?.Length;
        var column = Math.Min(caret.Column, length ?? int.MaxValue);
        var row = source.RowOfLine(caret.Line);
        var page = Math.Max(1, ViewportRows);
        switch (e.Key)
        {
            case Key.Left when column > 0: caret = caret with { Column = column - 1 }; break;
            case Key.Left when row > 0:
                var previous = source.Lines[row - 1];
                caret = new FilePosition(previous, LoadedText(previous)?.Length ?? 0);
                break;
            case Key.Right when length is null || column < length: caret = caret with { Column = column + 1 }; break;
            case Key.Right when row + 1 < source.RowCount: caret = new FilePosition(source.Lines[row + 1], 0); break;
            case Key.Left or Key.Right: return true; // at the start or end of the file
            case Key.Up: caret = ByRows(source, caret, -1); break;
            case Key.Down: caret = ByRows(source, caret, 1); break;
            case Key.PageUp: caret = ByRows(source, caret, -page); break;
            case Key.PageDown: caret = ByRows(source, caret, page); break;
            case Key.Home: caret = caret with { Column = 0 }; break;
            case Key.End when length is { } end: caret = caret with { Column = end }; break;
            case Key.End: return true; // the line is not read yet
            default: return false;
        }

        SetSelectionCaret(selection, caret, extend: true);
        return true;
    }
}
