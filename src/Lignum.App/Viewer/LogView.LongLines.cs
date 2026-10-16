using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Lignum.App.Themes;

namespace Lignum.App.Viewer;

/// <summary>
/// Long lines (ticket 07): rows cut at <see cref="LineSource.DisplayLimit"/> end with a clickable marker that
/// raises <see cref="FullLineRequested"/>. (The detail pane otherwise follows the caret; see LogTabViewModel.)
/// </summary>
public sealed partial class LogView
{
    /// <summary>Drawn after the text of a truncated row; clicking it asks for the full line.</summary>
    public const string TruncationMarker = " … [truncated: show full line]";

    public static readonly DependencyProperty MarkerForegroundProperty = DependencyProperty.Register(
        nameof(MarkerForeground), typeof(Brush), typeof(LogView),
        new FrameworkPropertyMetadata(SystemColors.HotTrackBrush, FrameworkPropertyMetadataOptions.AffectsRender));

    // Where the markers were drawn in the last render, for hit-testing.
    private readonly List<(Rect Bounds, long Line)> _markerHits = [];

    /// <summary>The user clicked a truncation marker: show this 0-based file line in full.</summary>
    public event EventHandler<long>? FullLineRequested;

    /// <summary>Colour of the truncation marker (themed like the line numbers).</summary>
    public Brush MarkerForeground
    {
        get => (Brush)GetValue(MarkerForegroundProperty);
        set => SetValue(MarkerForegroundProperty, value);
    }

    private void InitializeLongLines() => SetResourceReference(MarkerForegroundProperty, ThemeKeys.LineNumberForeground);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var point = e.GetPosition(this);
        foreach (var (bounds, line) in _markerHits)
        {
            if (!bounds.Contains(point)) continue;
            FullLineRequested?.Invoke(this, line);
            e.Handled = true;
            return;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        Cursor = _markerHits.Exists(h => h.Bounds.Contains(point)) ? Cursors.Hand : null;
    }

    /// <summary>
    /// Draws the marker after a truncated row's text, at display column <paramref name="markerColumn"/>, clipped to
    /// the text area. Called from <c>OnRender</c>.
    /// </summary>
    private void DrawTruncationMarker(DrawingContext dc, DisplayRow row, int markerColumn, double textLeft, double y,
        double pixelsPerDip)
    {
        var x = textLeft + (markerColumn - FirstColumn) * _charWidth;
        if (x >= ActualWidth) return;
        var marker = Format(TruncationMarker, MarkerForeground, pixelsPerDip);
        marker.SetFontStyle(FontStyles.Italic);
        var bounds = new Rect(x, y, marker.WidthIncludingTrailingWhitespace, _lineHeight);
        var visible = Rect.Intersect(bounds, new Rect(textLeft, 0, Math.Max(0, ActualWidth - textLeft), ActualHeight));
        if (visible.IsEmpty) return;

        dc.PushClip(new RectangleGeometry(visible));
        dc.DrawText(marker, new Point(x, y));
        dc.Pop();
        _markerHits.Add((visible, row.LineNumber));
    }
}
