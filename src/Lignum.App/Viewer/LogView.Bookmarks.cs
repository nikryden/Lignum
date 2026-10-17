using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Lignum.App.Themes;

namespace Lignum.App.Viewer;

/// <summary>
/// Bookmark markers (ticket 17): a dot in a gutter at the left of the line-number margin on every bookmarked
/// file line. The gutter is only there while <see cref="Bookmarks"/> is set.
/// </summary>
public sealed partial class LogView
{
    public static readonly DependencyProperty BookmarksProperty = DependencyProperty.Register(
        nameof(Bookmarks), typeof(IReadOnlySet<long>), typeof(LogView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BookmarkNotesProperty = DependencyProperty.Register(
        nameof(BookmarkNotes), typeof(IReadOnlyDictionary<long, string>), typeof(LogView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BookmarkBrushProperty = DependencyProperty.Register(
        nameof(BookmarkBrush), typeof(Brush), typeof(LogView),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Bookmarked 0-based file lines (replace the set to repaint), or null for no bookmark gutter.</summary>
    public IReadOnlySet<long>? Bookmarks
    {
        get => (IReadOnlySet<long>?)GetValue(BookmarksProperty);
        set => SetValue(BookmarksProperty, value);
    }

    /// <summary>Notes by 0-based file line (ticket 06): a noted bookmark gets a square marker and a tooltip.</summary>
    public IReadOnlyDictionary<long, string>? BookmarkNotes
    {
        get => (IReadOnlyDictionary<long, string>?)GetValue(BookmarkNotesProperty);
        set => SetValue(BookmarkNotesProperty, value);
    }

    public Brush BookmarkBrush
    {
        get => (Brush)GetValue(BookmarkBrushProperty);
        set => SetValue(BookmarkBrushProperty, value);
    }

    /// <summary>
    /// The line-number margin was double-clicked (ticket 04), with that row's 0-based file line; in the results
    /// pane that is the original line in the file.
    /// </summary>
    public event EventHandler<long>? MarginDoubleClicked;

    /// <summary>A left double-click on a row's line-number margin raises <see cref="MarginDoubleClicked"/>.</summary>
    private bool HandleMarginDoubleClick(MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || MarginLineAt(e.GetPosition(this)) is not { } line) return false;
        MarginDoubleClicked?.Invoke(this, line);
        e.Handled = true;
        return true;
    }

    /// <summary>The 0-based file line of the row whose line-number margin is under <paramref name="point"/>, or null.</summary>
    private long? MarginLineAt(Point point)
    {
        if (Source is not { } source || point.X >= _textLeft - TextPadding) return null;
        var row = Geometry.PointToPosition(point).Row;
        return row >= 0 && row < source.RowCount ? source.Lines[row] : null;
    }

    /// <summary>Shows a bookmark's note as the tooltip while the mouse is over its margin. Called from OnMouseMove.</summary>
    private void UpdateNoteToolTip(Point point)
    {
        var note = MarginLineAt(point) is { } line && BookmarkNotes is { } notes ? notes.GetValueOrDefault(line) : null;
        if (!Equals(ToolTip, note)) ToolTip = note;
    }

    private void InitializeBookmarks() => SetResourceReference(BookmarkBrushProperty, ThemeKeys.BookmarkMarker);

    /// <summary>Width of the marker gutter added to the margin: 0 without bookmarks.</summary>
    private double BookmarkGutterWidth => Bookmarks is null ? 0 : Math.Ceiling(_lineHeight * 0.8);

    /// <summary>Draws the marker for <paramref name="row"/> if its line is bookmarked. Called from OnRender.</summary>
    private void DrawBookmarkMarker(DrawingContext dc, DisplayRow row, double y)
    {
        if (Bookmarks is not { } bookmarks || !bookmarks.Contains(row.LineNumber)) return;
        var gutter = BookmarkGutterWidth;
        var radius = Math.Max(2, Math.Min(gutter, _lineHeight) * 0.3);
        var center = new Point(gutter / 2 + 2, y + _lineHeight / 2);
        if (BookmarkNotes?.ContainsKey(row.LineNumber) == true)
        {
            // A noted bookmark: a slightly larger square, so it stands out from the plain dots.
            var half = radius * 1.2;
            dc.DrawRectangle(BookmarkBrush, null, new Rect(center.X - half, center.Y - half, 2 * half, 2 * half));
            return;
        }

        dc.DrawEllipse(BookmarkBrush, null, center, radius, radius);
    }
}
