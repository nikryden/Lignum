using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lignum.App.Themes;
using Lignum.Core;

namespace Lignum.App.Viewer;

/// <summary>
/// Selection: the view draws and edits a <see cref="TextSelection"/> (file positions, so it survives
/// scrolling). Drag selects, Shift+Click extends, Shift+arrows/Home/End/PageUp/PageDown move the caret
/// and extend. Columns on screen are tab-expanded; they are mapped back to character indexes with
/// <see cref="TabExpansion"/>. Rows map to file lines through the source's line list, so the same code
/// works for the results pane.
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

    /// <summary>The selection shown and edited by this view. Null: no selection or caret.</summary>
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

    // ---- drawing (called from OnRender) ----

    /// <summary>Highlights the selected part of one row, behind its text.</summary>
    private void DrawRowSelection(DrawingContext dc, DisplayRow row, double y, double textLeft, int firstColumn)
    {
        if (Selection?.ColumnsOn(row.LineNumber) is not { } range) return;
        var text = row.Text;
        var from = TabExpansion.ToDisplayColumn(text, TabWidth, Math.Min(range.From, text.Length));
        var to = range.To > text.Length
            ? TabExpansion.ToDisplayColumn(text, TabWidth, text.Length) + 1 // the line break counts as one cell
            : TabExpansion.ToDisplayColumn(text, TabWidth, range.To);
        var left = textLeft + Math.Max(0, from - firstColumn) * _charWidth;
        var right = textLeft + Math.Max(0, (long)to - firstColumn) * _charWidth;
        if (right > left) dc.DrawRectangle(SelectionBackground, null, new Rect(left, y, right - left, _lineHeight));
    }

    /// <summary>A thin caret where the selection ends, while the view has keyboard focus.</summary>
    private void DrawCaret(DrawingContext dc, long firstRow, int rowCount, double textLeft, int firstColumn)
    {
        if (Selection is not { } selection || !IsKeyboardFocused) return;
        var caret = selection.Caret;
        for (var i = 0; i < rowCount; i++)
        {
            if (TryGetRow(firstRow + i) is not { } row || row.LineNumber != caret.Line) continue;
            var column = TabExpansion.ToDisplayColumn(row.Text, TabWidth, Math.Min(caret.Column, row.Text.Length));
            if (column < firstColumn) return;
            var x = Math.Round(textLeft + (column - firstColumn) * _charWidth);
            dc.DrawRectangle(Foreground, null, new Rect(x, i * _lineHeight, 1, _lineHeight));
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

    // ---- mouse ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (Selection is not { } selection || HitTest(e.GetPosition(this)) is not { } position) return;
        selection.MoveCaret(position, extend: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        _dragging = CaptureMouse();
        e.Handled = true;
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
        if (HitTest(clamped) is { } position) selection.MoveCaret(position, extend: true);
    }

    /// <summary>The file position nearest to a point in the view, or null when there are no rows.</summary>
    private FilePosition? HitTest(Point point)
    {
        if (Source is not { } source || source.RowCount == 0) return null;
        var position = Geometry.PointToPosition(point);
        var row = Math.Clamp(position.Row, 0, source.RowCount - 1);
        return new FilePosition(source.Lines[row], ToCharColumn(row, position.Column));
    }

    // ---- keyboard ----

    /// <summary>Shift+arrows, Home, End, PageUp, PageDown move the caret and extend the selection.</summary>
    private bool HandleSelectionKey(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Shift || Selection is not { } selection) return false;
        if (Source is not { } source || source.RowCount == 0) return false;

        var caret = selection.Caret;
        var row = RowOf(source.Lines, caret.Line);
        var column = ClampColumn(row, caret.Column);
        switch (e.Key)
        {
            case Key.Left when column > 0: column--; break;
            case Key.Left when row > 0: row--; column = LineLength(row) ?? int.MaxValue; break;
            case Key.Left: break;
            case Key.Right when column < (LineLength(row) ?? int.MaxValue): column++; break;
            case Key.Right when row + 1 < source.RowCount: row++; column = 0; break;
            case Key.Right: break;
            case Key.Up: row = Math.Max(0, row - 1); break;
            case Key.Down: row = Math.Min(source.RowCount - 1, row + 1); break;
            case Key.PageUp: row = Math.Max(0, row - Math.Max(1, ViewportRows)); break;
            case Key.PageDown: row = Math.Min(source.RowCount - 1, row + Math.Max(1, ViewportRows)); break;
            case Key.Home: column = 0; break;
            case Key.End: column = LineLength(row) ?? int.MaxValue; break;
            default: return false;
        }

        selection.MoveCaret(new FilePosition(source.Lines[row], ClampColumn(row, column)), extend: true);
        EnsureVisible(row, column);
        return true;
    }

    /// <summary>Scrolls just enough to show a row and (when its text is loaded) a character column.</summary>
    private void EnsureVisible(long row, int column)
    {
        var rows = Math.Max(1, ViewportRows);
        if (row < FirstRow) SetCurrentValue(FirstRowProperty, row);
        else if (row >= FirstRow + rows) SetCurrentValue(FirstRowProperty, row - rows + 1);

        if (TryGetRow(row) is not { } loaded) return;
        var display = TabExpansion.ToDisplayColumn(loaded.Text, TabWidth, Math.Min(column, loaded.Text.Length));
        var columns = Math.Max(1, ViewportColumns);
        if (display < FirstColumn) FirstColumn = display;
        else if (display >= FirstColumn + columns) FirstColumn = display - columns + 1;
    }

    /// <summary>The row showing <paramref name="line"/> (or the nearest one before it): rows are in line order.</summary>
    private static long RowOf(ILineList lines, long line)
    {
        long lo = 0, hi = lines.Count - 1;
        while (lo < hi)
        {
            var mid = lo + (hi - lo + 1) / 2;
            if (lines[mid] <= line) lo = mid;
            else hi = mid - 1;
        }

        return Math.Max(0, lo);
    }

    private int? LineLength(long row) => TryGetRow(row)?.Text.Length;

    private int ClampColumn(long row, int column) => LineLength(row) is { } length ? Math.Min(column, length) : column;

    /// <summary>A display column on a row as a character index (tabs expanded); unloaded rows assume no tabs.</summary>
    private int ToCharColumn(long row, int displayColumn) =>
        TryGetRow(row) is { } loaded ? TabExpansion.ToCharIndex(loaded.Text, TabWidth, displayColumn) : displayColumn;
}
