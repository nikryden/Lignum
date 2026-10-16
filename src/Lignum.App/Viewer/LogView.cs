using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Lignum.App.Themes;

namespace Lignum.App.Viewer;

/// <summary>
/// The custom virtualized log viewer. It draws only the rows that fit on screen, starting at
/// <see cref="FirstRow"/>, from a <see cref="LineSource"/> (a list of line numbers plus a line reader),
/// with a line-number margin on the left. Row text is read on a background thread and drawn when it
/// arrives, so the UI thread never touches the file. Used for the main view and, later, the results pane.
/// </summary>
public sealed partial class LogView : FrameworkElement
{
    private const double MarginPadding = 8;
    private const double TextPadding = 6;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(LineSource), typeof(LogView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

    public static readonly DependencyProperty FirstRowProperty = DependencyProperty.Register(
        nameof(FirstRow), typeof(long), typeof(LogView),
        new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender, OnFirstRowChanged,
            CoerceFirstRow));

    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(LogView),
        new FrameworkPropertyMetadata(new FontFamily("Cascadia Mono, Consolas"),
            FrameworkPropertyMetadataOptions.AffectsRender, OnFontChanged));

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(LogView),
        new FrameworkPropertyMetadata(13.0, FrameworkPropertyMetadataOptions.AffectsRender, OnFontChanged));

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(LogView),
        new FrameworkPropertyMetadata(SystemColors.WindowBrush, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(LogView),
        new FrameworkPropertyMetadata(SystemColors.WindowTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineNumberForegroundProperty = DependencyProperty.Register(
        nameof(LineNumberForeground), typeof(Brush), typeof(LogView),
        new FrameworkPropertyMetadata(SystemColors.GrayTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineNumberBackgroundProperty = DependencyProperty.Register(
        nameof(LineNumberBackground), typeof(Brush), typeof(LogView),
        new FrameworkPropertyMetadata(SystemColors.ControlBrush, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TabWidthProperty = DependencyProperty.Register(
        nameof(TabWidth), typeof(int), typeof(LogView),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender));

    // Rows read so far: _rows[i] is row _rowsFirst + i. Replaced wholesale when a read completes.
    private IReadOnlyList<DisplayRow> _rows = [];
    private long _rowsFirst;
    private long _rowCountAtLoad;
    private (long First, int Count)? _pendingLoad;
    private CancellationTokenSource? _loadCancel;
    private int _changePosted;
    private Typeface? _typeface;
    private double _charWidth;
    private double _lineHeight;

    public LogView()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        // Theme brushes (ticket 13); without the theme resources (tests) the system-brush defaults stay.
        SetResourceReference(BackgroundProperty, ThemeKeys.ViewerBackground);
        SetResourceReference(ForegroundProperty, ThemeKeys.ViewerForeground);
        SetResourceReference(LineNumberBackgroundProperty, ThemeKeys.LineNumberBackground);
        SetResourceReference(LineNumberForegroundProperty, ThemeKeys.LineNumberForeground);
        InitializeScrolling();
        InitializeSelection();
    }

    /// <summary>Columns between tab stops.</summary>
    public int TabWidth
    {
        get => (int)GetValue(TabWidthProperty);
        set => SetValue(TabWidthProperty, value);
    }

    /// <summary>The rows to show. Null shows an empty view.</summary>
    public LineSource? Source
    {
        get => (LineSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>The row drawn at the top of the view; the vertical scroll offset. Clamped to the rows available.</summary>
    public long FirstRow
    {
        get => (long)GetValue(FirstRowProperty);
        set => SetValue(FirstRowProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public Brush Background
    {
        get => (Brush)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush LineNumberForeground
    {
        get => (Brush)GetValue(LineNumberForegroundProperty);
        set => SetValue(LineNumberForegroundProperty, value);
    }

    public Brush LineNumberBackground
    {
        get => (Brush)GetValue(LineNumberBackgroundProperty);
        set => SetValue(LineNumberBackgroundProperty, value);
    }

    /// <summary>Height of one row in device-independent pixels.</summary>
    public double LineHeight
    {
        get
        {
            EnsureFontMetrics();
            return _lineHeight;
        }
    }

    /// <summary>Number of rows that fit in the current height, counting a partly visible last row.</summary>
    public int VisibleRowCapacity => LineHeight <= 0 ? 0 : (int)Math.Ceiling(ActualHeight / LineHeight);

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

    protected override void OnRender(DrawingContext dc)
    {
        EnsureFontMetrics();
        var width = ActualWidth;
        var height = ActualHeight;
        dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));

        var source = Source;
        if (source is null) return;

        var first = FirstRow;
        var wanted = (int)Math.Min(VisibleRowCapacity, Math.Max(0, source.RowCount - first));
        EnsureRowsLoaded(source, first, wanted);

        // The margin fits the widest line number on screen (1-based), with at least 4 digits.
        var lastVisibleLine = 0L;
        for (var i = 0; i < wanted; i++)
        {
            if (TryGetRow(first + i) is { } r) lastVisibleLine = Math.Max(lastVisibleLine, r.LineNumber);
        }

        var digits = Math.Max(4, (lastVisibleLine + 1).ToString(CultureInfo.InvariantCulture).Length);
        var marginWidth = Math.Ceiling(digits * _charWidth + 2 * MarginPadding);
        dc.DrawRectangle(LineNumberBackground, null, new Rect(0, 0, marginWidth, height));

        var textLeft = marginWidth + TextPadding;
        UpdateTextLeft(textLeft);
        var firstColumn = FirstColumn;
        // Draw only the characters that can be visible; long-line truncation proper is ticket 07.
        var maxChars = (int)Math.Ceiling(Math.Max(0, width - textLeft) / _charWidth) + 1;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var i = 0; i < wanted; i++)
        {
            if (TryGetRow(first + i) is not { } row) continue;
            var y = i * _lineHeight;

            var number = Format((row.LineNumber + 1).ToString(CultureInfo.InvariantCulture),
                LineNumberForeground, pixelsPerDip);
            dc.DrawText(number, new Point(marginWidth - MarginPadding - number.WidthIncludingTrailingWhitespace, y));

            DrawRowSelection(dc, row, y, textLeft, firstColumn);
            if (row.Text.Length == 0) continue;
            // Columns are tab-expanded display columns; expand up to the right edge, then skip the scrolled-off part.
            var expanded = TabExpansion.Expand(row.Text, TabWidth, (int)Math.Min((long)firstColumn + maxChars, int.MaxValue));
            if (expanded.Length <= firstColumn) continue;
            var text = firstColumn == 0 ? expanded : expanded[firstColumn..];
            dc.DrawText(Format(text, Foreground, pixelsPerDip), new Point(textLeft, y));
        }

        DrawCaret(dc, first, wanted, textLeft);
    }

    private FormattedText Format(string text, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface!, FontSize, brush,
            pixelsPerDip);

    private DisplayRow? TryGetRow(long row)
    {
        var i = row - _rowsFirst;
        return i >= 0 && i < _rows.Count ? _rows[(int)i] : null;
    }

    /// <summary>Starts a background read if the rows on screen are not loaded (or were incomplete).</summary>
    private void EnsureRowsLoaded(LineSource source, long first, int count)
    {
        if (count == 0) return;
        var covered = first >= _rowsFirst && first + count <= _rowsFirst + _rows.Count;
        // A row that was the last one known when read may have been only partly indexed.
        var touchesOldEnd = first + count >= _rowCountAtLoad && source.RowCount > _rowCountAtLoad;
        if (covered && !touchesOldEnd) return;
        if (_pendingLoad == (first, count)) return;

        LoadRows(source, first, count);
    }

    private async void LoadRows(LineSource source, long first, int count)
    {
        _loadCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _loadCancel = cancel;
        _pendingLoad = (first, count);
        var rowCount = source.RowCount;
        try
        {
            var rows = await source.ReadRowsAsync(first, count, cancel.Token);
            if (cancel.IsCancellationRequested || !ReferenceEquals(source, Source)) return;
            _rows = rows;
            _rowsFirst = first;
            _rowCountAtLoad = rowCount;
            OnRowsLoaded(rows);
            InvalidateVisual();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or ArgumentOutOfRangeException)
        {
            // The document was closed or changed under us; the next render tries again.
        }
        finally
        {
            if (ReferenceEquals(_loadCancel, cancel))
            {
                _pendingLoad = null;
                _loadCancel = null;
            }

            cancel.Dispose();
        }
    }

    private void EnsureFontMetrics()
    {
        if (_typeface is not null) return;
        _typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var probe = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface,
            FontSize, Brushes.Black, 1.0);
        _charWidth = probe.WidthIncludingTrailingWhitespace;
        _lineHeight = Math.Ceiling(FontFamily.LineSpacing * FontSize);
    }

    private static void OnFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (LogView)d;
        view._typeface = null;
        view.OnViewportChanged(); // zoom changes how many rows and columns fit
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (LogView)d;
        if (e.OldValue is LineSource old) old.Changed -= view.OnSourceRowsChanged;
        view._loadCancel?.Cancel();
        view._rows = [];
        view._rowsFirst = 0;
        view._rowCountAtLoad = 0;
        view._pendingLoad = null;
        view.ResetScrolling();
        if (e.NewValue is LineSource source) source.Changed += view.OnSourceRowsChanged;
    }

    /// <summary>Rows were added (on a background thread): redraw once, however many events arrive.</summary>
    private void OnSourceRowsChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _changePosted, 1) != 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            Volatile.Write(ref _changePosted, 0);
            ScrollOwner?.InvalidateScrollInfo(); // the extent grew; FirstRow stays put
            // Only a view that reaches the end of what was loaded can change; the rest stays as drawn.
            if (FirstRow + VisibleRowCapacity >= _rowCountAtLoad) InvalidateVisual();
        });
    }
}
