namespace Lignum.App.Themes;

/// <summary>
/// Resource keys of the theme brushes defined in <c>Themes/Light.xaml</c> and <c>Themes/Dark.xaml</c>.
/// In XAML use <c>{DynamicResource ViewerBackgroundBrush}</c> (DynamicResource, never StaticResource,
/// so a theme switch repaints); in code use <c>SetResourceReference(prop, ThemeKeys.X)</c>.
/// Adding a brush: add the key here and a brush with that key to BOTH theme files.
/// </summary>
public static class ThemeKeys
{
    public const string WindowBackground = "WindowBackgroundBrush";
    public const string WindowForeground = "WindowForegroundBrush";

    public const string ViewerBackground = "ViewerBackgroundBrush";
    public const string ViewerForeground = "ViewerForegroundBrush";
    public const string LineNumberBackground = "LineNumberBackgroundBrush";
    public const string LineNumberForeground = "LineNumberForegroundBrush";
    public const string SelectionBackground = "SelectionBackgroundBrush";
    public const string SearchMatchBackground = "SearchMatchBackgroundBrush";

    public const string PaneBackground = "PaneBackgroundBrush";
    public const string PaneBorder = "PaneBorderBrush";

    public const string StatusBarBackground = "StatusBarBackgroundBrush";
    public const string StatusBarForeground = "StatusBarForegroundBrush";
    public const string ErrorForeground = "ErrorForegroundBrush";

    public const string BannerBackground = "BannerBackgroundBrush";
    public const string BannerBorder = "BannerBorderBrush";
    public const string BannerForeground = "BannerForegroundBrush";

    public const string TabNeedsUpdate = "TabNeedsUpdateBrush";
    public const string TabAutoUpdate = "TabAutoUpdateBrush";

    public const string FilterIncludeBackground = "FilterIncludeBackgroundBrush";
    public const string FilterIncludeBorder = "FilterIncludeBorderBrush";
    public const string FilterExcludeBackground = "FilterExcludeBackgroundBrush";
    public const string FilterExcludeBorder = "FilterExcludeBorderBrush";

    /// <summary>The bookmark dot in the viewer margin (ticket 17).</summary>
    public const string BookmarkMarker = "BookmarkMarkerBrush";

    /// <summary>The marker brush for a bookmark color; Blue is <see cref="BookmarkMarker"/>.</summary>
    public static string BookmarkMarkerFor(Settings.BookmarkColor color) =>
        color == Settings.BookmarkColor.Blue ? BookmarkMarker : $"BookmarkMarker{color}Brush";
}
