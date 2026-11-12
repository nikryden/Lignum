using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lignum.App.Views;

/// <summary>
/// Menu icons (ticket 35): <c>views:MenuIcon.Glyph="&amp;#xE8E5;"</c> on a <see cref="MenuItem"/> sets its icon to that
/// Segoe Fluent Icons glyph, the font the toolbar uses. The glyph takes the item's foreground, so it follows the
/// theme and dims with a disabled item.
/// </summary>
public static class MenuIcon
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(MenuIcon), new PropertyMetadata(null, OnGlyphChanged));

    public static string? GetGlyph(DependencyObject element) => (string?)element.GetValue(GlyphProperty);

    public static void SetGlyph(DependencyObject element, string? value) => element.SetValue(GlyphProperty, value);

    private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MenuItem item) return;
        item.Icon = e.NewValue is string { Length: > 0 } glyph
            ? new TextBlock
            {
                Text = glyph,
                FontFamily = IconFont,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : null;
    }
}
