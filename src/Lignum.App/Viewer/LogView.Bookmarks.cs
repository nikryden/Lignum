using System.Windows;
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

    public static readonly DependencyProperty BookmarkBrushProperty = DependencyProperty.Register(
        nameof(BookmarkBrush), typeof(Brush), typeof(LogView),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Bookmarked 0-based file lines (replace the set to repaint), or null for no bookmark gutter.</summary>
    public IReadOnlySet<long>? Bookmarks
    {
        get => (IReadOnlySet<long>?)GetValue(BookmarksProperty);
        set => SetValue(BookmarksProperty, value);
    }

    public Brush BookmarkBrush
    {
        get => (Brush)GetValue(BookmarkBrushProperty);
        set => SetValue(BookmarkBrushProperty, value);
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
        dc.DrawEllipse(BookmarkBrush, null, new Point(gutter / 2 + 2, y + _lineHeight / 2), radius, radius);
    }
}
