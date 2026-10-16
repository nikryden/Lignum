using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lignum.App.Viewer;

/// <summary>
/// Scrolling: <see cref="LogView"/> is its own <see cref="IScrollInfo"/>, so host it in a
/// <c>ScrollViewer</c> with <c>CanContentScroll="True"</c> for the scrollbars. Scroll units are rows
/// (vertical: offset = <see cref="FirstRow"/>, extent = row count) and character columns
/// (horizontal: offset = <see cref="FirstColumn"/>, extent = the longest line read so far).
/// Wheel, arrow keys and PageUp/PageDown are handled here; Ctrl+wheel is left for zoom.
/// </summary>
public sealed partial class LogView : IScrollInfo
{
    private WheelAccumulator _wheel = new();
    private int _firstColumn;
    private int _longestLine;
    private double _textLeft;
    private int _scrollInfoPosted;

    private void InitializeScrolling()
    {
        Focusable = true;
        FocusVisualStyle = null;
    }

    /// <summary>The first character column drawn; the horizontal scroll offset. Clamped to the longest line.</summary>
    public int FirstColumn
    {
        get => _firstColumn;
        set
        {
            var clamped = (int)ViewportMath.ClampFirst(value, _longestLine, ViewportColumns);
            if (clamped == _firstColumn) return;
            _firstColumn = clamped;
            InvalidateVisual();
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    /// <summary>Rows that fit completely in the view (at least 1 once it has any height). One page.</summary>
    public int ViewportRows => ViewportMath.FullyVisible(ActualHeight, LineHeight);

    /// <summary>Character columns that fit completely right of the line-number margin.</summary>
    public int ViewportColumns
    {
        get
        {
            EnsureFontMetrics();
            var textLeft = _textLeft > 0 ? _textLeft : Math.Ceiling(4 * _charWidth + 2 * MarginPadding) + TextPadding;
            return ViewportMath.FullyVisible(ActualWidth - textLeft, _charWidth);
        }
    }

    /// <summary>The current layout and scroll offsets, for hit-testing (selection, caret, context menu).</summary>
    public ViewportGeometry Geometry
    {
        get
        {
            EnsureFontMetrics();
            return new ViewportGeometry(FirstRow, FirstColumn, _textLeft, _lineHeight, _charWidth);
        }
    }

    /// <summary>Scrolls by whole rows; positive moves towards the end. Clamped.</summary>
    public void ScrollRows(long delta) => SetCurrentValue(FirstRowProperty, FirstRow + delta);

    /// <summary>Scrolls by whole columns; positive moves right. Clamped.</summary>
    public void ScrollColumns(int delta) => FirstColumn = (int)Math.Clamp((long)FirstColumn + delta, 0, int.MaxValue);

    // ---- IScrollInfo ----

    public bool CanVerticallyScroll { get; set; } = true;
    public bool CanHorizontallyScroll { get; set; } = true;
    public double ExtentHeight => Source?.RowCount ?? 0;
    public double ExtentWidth => _longestLine;
    public double ViewportHeight => ViewportRows;
    public double ViewportWidth => ViewportColumns;
    public double VerticalOffset => FirstRow;
    public double HorizontalOffset => FirstColumn;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => ScrollRows(-1);
    public void LineDown() => ScrollRows(1);
    public void PageUp() => ScrollRows(-Math.Max(1, ViewportRows));
    public void PageDown() => ScrollRows(Math.Max(1, ViewportRows));
    public void MouseWheelUp() => ScrollRows(-WheelLines);
    public void MouseWheelDown() => ScrollRows(WheelLines);
    public void LineLeft() => ScrollColumns(-1);
    public void LineRight() => ScrollColumns(1);
    public void PageLeft() => ScrollColumns(-Math.Max(1, ViewportColumns));
    public void PageRight() => ScrollColumns(Math.Max(1, ViewportColumns));
    public void MouseWheelLeft() => ScrollColumns(-WheelLines);
    public void MouseWheelRight() => ScrollColumns(WheelLines);

    public void SetVerticalOffset(double offset)
    {
        if (!CanVerticallyScroll) return;
        SetCurrentValue(FirstRowProperty, ViewportMath.OffsetToFirst(offset, Source?.RowCount ?? 0, ViewportRows));
    }

    public void SetHorizontalOffset(double offset)
    {
        if (!CanHorizontallyScroll) return;
        FirstColumn = (int)ViewportMath.OffsetToFirst(offset, _longestLine, ViewportColumns);
    }

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    // ---- input ----

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        // MainWindow handles Ctrl+wheel (zoom) in PreviewMouseWheel; don't scroll if anyone else lets it through.
        if (e.Handled || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        var rows = _wheel.Add(e.Delta, WheelLines);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) ScrollColumns(rows);
        else ScrollRows(rows);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        switch (e.Key)
        {
            case Key.Up: LineUp(); break;
            case Key.Down: LineDown(); break;
            case Key.PageUp: PageUp(); break;
            case Key.PageDown: PageDown(); break;
            case Key.Left: LineLeft(); break;
            case Key.Right: LineRight(); break;
            default: return;
        }

        e.Handled = true;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        OnViewportChanged();
    }

    /// <summary>The view's size or font changed: re-clamp the offsets and tell the ScrollViewer.</summary>
    private void OnViewportChanged()
    {
        CoerceValue(FirstRowProperty);
        FirstColumn = _firstColumn;
        ScrollOwner?.InvalidateScrollInfo();
    }

    // ---- hooks called from LogView.cs ----

    private static int WheelLines => SystemParameters.WheelScrollLines > 0 ? SystemParameters.WheelScrollLines : 3;

    private static object CoerceFirstRow(DependencyObject d, object value)
    {
        var view = (LogView)d;
        return view.Source is { } source ? ViewportMath.ClampFirst((long)value, source.RowCount, view.ViewportRows) : 0L;
    }

    private static void OnFirstRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LogView)d).ScrollOwner?.InvalidateScrollInfo();

    /// <summary>
    /// Widens the horizontal extent to the longest line read so far, in display columns. Tabs count as a full
    /// tab stop (an upper bound that avoids expanding long lines on the UI thread).
    /// </summary>
    private void OnRowsLoaded(IReadOnlyList<DisplayRow> rows)
    {
        var longest = _longestLine;
        var extraPerTab = Math.Max(1, TabWidth) - 1;
        foreach (var row in rows)
        {
            var text = row.Text;
            var tabs = extraPerTab == 0 ? 0 : text.AsSpan().Count('\t');
            var marker = row.IsTruncated ? TruncationMarker.Length : 0; // ticket 07: the marker can be scrolled to
            longest = (int)Math.Min(int.MaxValue, Math.Max(longest, text.Length + (long)tabs * extraPerTab + marker));
        }

        if (longest == _longestLine) return;
        _longestLine = longest;
        ScrollOwner?.InvalidateScrollInfo();
    }

    /// <summary>The margin width follows the line-number digits on screen, which changes the visible columns.</summary>
    private void UpdateTextLeft(double textLeft)
    {
        if (textLeft == _textLeft) return;
        _textLeft = textLeft;
        // Called from OnRender: tell the ScrollViewer after this pass rather than invalidating layout mid-render.
        if (Interlocked.Exchange(ref _scrollInfoPosted, 1) != 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scrollInfoPosted = 0;
            ScrollOwner?.InvalidateScrollInfo();
        });
    }

    /// <summary>A new source starts at the top-left.</summary>
    private void ResetScrolling()
    {
        _wheel = new WheelAccumulator();
        _longestLine = 0;
        _firstColumn = 0;
        SetCurrentValue(FirstRowProperty, 0L);
        ScrollOwner?.InvalidateScrollInfo();
    }
}
