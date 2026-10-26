using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Lignum.App.Settings;
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

    public static readonly DependencyProperty BookmarkColorsProperty = DependencyProperty.Register(
        nameof(BookmarkColors), typeof(IReadOnlyDictionary<long, BookmarkColor>), typeof(LogView),
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

    /// <summary>Colors by 0-based file line; a bookmark that is not in it is drawn with <see cref="BookmarkBrush"/> (Blue).</summary>
    public IReadOnlyDictionary<long, BookmarkColor>? BookmarkColors
    {
        get => (IReadOnlyDictionary<long, BookmarkColor>?)GetValue(BookmarkColorsProperty);
        set => SetValue(BookmarkColorsProperty, value);
    }

    /// <summary>The Blue marker, from the theme. A theme switch replaces it, which also repaints the other colors.</summary>
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

    private System.Windows.Controls.Primitives.Popup? _noteBalloon;
    private System.Windows.Controls.TextBlock? _noteText;
    private long _noteLine = -1;

    /// <summary>
    /// Shows a bookmark's note in a balloon next to the mouse while it is over that row's margin (a tooltip would not
    /// reopen when only its text changes). Called from OnMouseMove.
    /// </summary>
    private void UpdateNoteToolTip(Point point)
    {
        var line = MarginLineAt(point);
        // Only shown bookmarks: the tab can hide some colors (ticket 24).
        var note = line is { } l && Bookmarks?.Contains(l) == true && BookmarkNotes is { } notes ? notes.GetValueOrDefault(l) : null;
        if (note is null)
        {
            HideNoteBalloon();
            return;
        }

        var balloon = _noteBalloon ??= CreateNoteBalloon();
        if (_noteLine != line || !balloon.IsOpen)
        {
            _noteLine = line!.Value;
            _noteText!.Text = note;
            balloon.HorizontalOffset = point.X + 18;
            balloon.VerticalOffset = point.Y + 14;
            balloon.IsOpen = true;
        }
    }

    private void HideNoteBalloon()
    {
        _noteLine = -1;
        if (_noteBalloon is { IsOpen: true } balloon) balloon.IsOpen = false;
    }

    private System.Windows.Controls.Primitives.Popup CreateNoteBalloon()
    {
        _noteText = new System.Windows.Controls.TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        _noteText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "WindowForegroundBrush");
        var border = new System.Windows.Controls.Border
        {
            Child = _noteText,
            MaxWidth = 420,
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.3 },
        };
        border.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "PaneBackgroundBrush");
        border.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "BookmarkMarkerBrush");
        MouseLeave += (_, _) => HideNoteBalloon();
        return new System.Windows.Controls.Primitives.Popup
        {
            Child = border,
            AllowsTransparency = true,
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
            StaysOpen = true,
            IsHitTestVisible = false,
        };
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
        var brush = BookmarkColors?.GetValueOrDefault(row.LineNumber) is { } color and not BookmarkColor.Blue
            && TryFindResource(ThemeKeys.BookmarkMarkerFor(color)) is Brush colored
            ? colored
            : BookmarkBrush;
        if (BookmarkNotes?.ContainsKey(row.LineNumber) == true)
        {
            // A noted bookmark: a slightly larger square, so it stands out from the plain dots.
            var half = radius * 1.2;
            dc.DrawRectangle(brush, null, new Rect(center.X - half, center.Y - half, 2 * half, 2 * half));
            return;
        }

        dc.DrawEllipse(brush, null, center, radius, radius);
    }
}
