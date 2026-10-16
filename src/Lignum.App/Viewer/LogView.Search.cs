using System.Windows;
using System.Windows.Media;
using Lignum.App.Themes;
using Lignum.Core;

namespace Lignum.App.Viewer;

/// <summary>
/// Search decorations: the text matched by <see cref="Highlight"/> gets a background in every visible row,
/// and the file line <see cref="MarkedLine"/> (the result the user went to) gets a row background. Both
/// views (main and results) use the same properties, keyed by file line number, not by row.
/// </summary>
public sealed partial class LogView
{
    public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight), typeof(Query), typeof(LogView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarkedLineProperty = DependencyProperty.Register(
        nameof(MarkedLine), typeof(long), typeof(LogView),
        new FrameworkPropertyMetadata(-1L, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SearchMatchBackgroundProperty = DependencyProperty.Register(
        nameof(SearchMatchBackground), typeof(Brush), typeof(LogView),
        new FrameworkPropertyMetadata(Brushes.Gold, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarkedLineBackgroundProperty = DependencyProperty.Register(
        nameof(MarkedLineBackground), typeof(Brush), typeof(LogView),
        new FrameworkPropertyMetadata(SystemColors.HighlightBrush, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The query whose matches are highlighted, or null.</summary>
    public Query? Highlight
    {
        get => (Query?)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    /// <summary>0-based file line drawn with <see cref="MarkedLineBackground"/>; -1 for none.</summary>
    public long MarkedLine
    {
        get => (long)GetValue(MarkedLineProperty);
        set => SetValue(MarkedLineProperty, value);
    }

    public Brush SearchMatchBackground
    {
        get => (Brush)GetValue(SearchMatchBackgroundProperty);
        set => SetValue(SearchMatchBackgroundProperty, value);
    }

    public Brush MarkedLineBackground
    {
        get => (Brush)GetValue(MarkedLineBackgroundProperty);
        set => SetValue(MarkedLineBackgroundProperty, value);
    }

    /// <summary>The source row under a point in this view, or null when there is no row there.</summary>
    public long? RowAt(Point point)
    {
        if (Source is not { } source || point.Y < 0) return null;
        var row = Geometry.PointToPosition(point).Row;
        return row >= 0 && row < source.RowCount ? row : null;
    }

    /// <summary>Scrolls so <paramref name="row"/> is visible, centring it when it was off screen.</summary>
    public void BringRowIntoView(long row)
    {
        var page = Math.Max(1, ViewportRows);
        if (row >= FirstRow && row < FirstRow + page) return;
        SetCurrentValue(FirstRowProperty, Math.Max(0, row - page / 2));
    }

    private void InitializeSearch()
    {
        SetResourceReference(SearchMatchBackgroundProperty, ThemeKeys.SearchMatchBackground);
        SetResourceReference(MarkedLineBackgroundProperty, ThemeKeys.SelectionBackground);
    }

    /// <summary>Draws a row's backgrounds (marked line, search matches) under its text. Called from OnRender.</summary>
    private void DrawRowDecorations(DrawingContext dc, DisplayRow row, double y, double textLeft, int firstColumn,
        int maxColumns)
    {
        if (row.LineNumber == MarkedLine)
        {
            dc.DrawRectangle(MarkedLineBackground, null, new Rect(textLeft, y, Math.Max(0, ActualWidth - textLeft), _lineHeight));
        }

        if (Highlight is not { } query || row.Text.Length == 0) return;
        var lastColumn = firstColumn + maxColumns;
        foreach (var match in query.FindMatches(row.Text))
        {
            var start = TabExpansion.ColumnOf(row.Text, match.Start, TabWidth);
            if (start >= lastColumn) break;
            var end = TabExpansion.ColumnOf(row.Text, match.Start + match.Length, TabWidth);
            if (end <= firstColumn) continue;
            var left = textLeft + (Math.Max(start, firstColumn) - firstColumn) * _charWidth;
            var right = textLeft + (Math.Min(end, lastColumn) - firstColumn) * _charWidth;
            dc.DrawRectangle(SearchMatchBackground, null, new Rect(left, y, right - left, _lineHeight));
        }
    }
}
