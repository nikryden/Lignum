using System.Windows;
using System.Windows.Media;

namespace Lignum.App.Viewer;

/// <summary>
/// Line coloring (ticket 14): each row is colored by the first matching rule of <see cref="LineHighlighter"/>.
/// The background is drawn first, then the search decorations (marked line, matches) on top, then the text in
/// the rule's foreground. Styles are resolved once per loaded row set, not on every redraw.
/// </summary>
public sealed partial class LogView
{
    public static readonly DependencyProperty LineHighlighterProperty = DependencyProperty.Register(
        nameof(LineHighlighter), typeof(LineHighlighter), typeof(LogView),
        new FrameworkPropertyMetadata(LineHighlighter.None, FrameworkPropertyMetadataOptions.AffectsRender));

    // Resolved styles for _styledRows (the _rows list they were computed for); null entries are unresolved.
    private IReadOnlyList<DisplayRow>? _styledRows;
    private LineHighlighter? _styledWith;
    private LineStyle?[] _styles = [];
    private bool[] _resolved = [];

    /// <summary>Whole-line coloring rules; <see cref="Viewer.LineHighlighter.None"/> for none.</summary>
    public LineHighlighter LineHighlighter
    {
        get => (LineHighlighter)GetValue(LineHighlighterProperty);
        set => SetValue(LineHighlighterProperty, value);
    }

    /// <summary>The rule style of a loaded row, or null. Called from OnRender for visible rows only.</summary>
    private LineStyle? LineStyleFor(long row, DisplayRow displayRow)
    {
        var highlighter = LineHighlighter ?? LineHighlighter.None;
        if (highlighter.IsEmpty) return null;

        if (!ReferenceEquals(_styledRows, _rows) || !ReferenceEquals(_styledWith, highlighter))
        {
            _styledRows = _rows;
            _styledWith = highlighter;
            _styles = new LineStyle?[_rows.Count];
            _resolved = new bool[_rows.Count];
        }

        var i = row - _rowsFirst;
        if (i < 0 || i >= _styles.Length) return highlighter.Resolve(displayRow.Text);
        if (!_resolved[i])
        {
            _styles[i] = highlighter.Resolve(displayRow.Text);
            _resolved[i] = true;
        }

        return _styles[i];
    }

    private void DrawLineBackground(DrawingContext dc, LineStyle? style, double y, double textLeft)
    {
        if (style?.Background is not { } background) return;
        dc.DrawRectangle(background, null, new Rect(textLeft, y, Math.Max(0, ActualWidth - textLeft), _lineHeight));
    }
}
